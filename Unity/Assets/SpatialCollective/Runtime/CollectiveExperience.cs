using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace SpatialCollective
{
    public sealed partial class CollectiveExperience : MonoBehaviour
    {
        // ===== Core runtime references (scene dependencies) =====
        // Deformation and mesh-rebuild core.
        public CollectiveRig rig;
        // Unified input reader.
        public CollectiveInputReader inputReader;
        // Preview/apply/undo/compare state controller.
        public CollectiveStateController stateController;
        // Keyframe demonstration controller.
        public CollectiveMotionStudy study;
        // Offscreen overview and detail camera controller.
        public CollectiveLinkedViews linkedViews;
        // Engineering/read-only dashboard controller.
        public CollectiveDashboard dashboard;
        // Synthetic gesture rehearsal controller.
        public CollectiveGestureRehearsal rehearsal;
        // Main headset or desktop camera.
        public Camera head;
        // XR tracking origin.
        public Transform trackingOrigin;
        // UI, gesture and skybox materials.
        public Material uiMaterial, handMaterial, insetMaterial, cabinSky, marsSky;
        // Font asset.
        public Font font;
        // Selected region index (0..5).
        public int selectedRegion;
        // Hand travel per parameter unit and overview scale.
        public float handTravel = .35f, overviewScale = .026f;
        // Panel root, assigned in Inspector or created at runtime when absent.
        [SerializeField] private Transform menu;
        // Whether to process physical hardware input; disabled during rehearsal.
        public bool ProcessHardwareInput = true;
        // Whether a fade-based view transition is in progress.
        public bool IsTransitioning { get; private set; }
        // Dragging includes control drags, spatial edits, cabin-entry holds and view-switch holds.
        public bool IsDragging => dragging || SpatialEditing || jumpHold > 0 || viewHold > 0;
        // Whether traditional bimanual editing or spatial Span is active.
        public bool IsBimanual => bimanual.Active || spatialMode == SpatialMode.Span;
        // Whether the headset read-only workspace layout is active.
        public bool HeadsetLayout => dashboard != null && dashboard.HeadsetLayout;
        // Room-scale helper component.
        public CollectiveRoomScale roomScale;
        // Public menu-root reference.
        public Transform Menu => menu;
        // Public current feedback text.
        public string Feedback => feedback;
        // Linked-view inspection node; -1 means unspecified.
        public int InspectionNode { get; private set; } = -1;
        // Selected parameter name, preferring a control caption over the region name.
        public string SelectedParameter => selectedControl != null ? selectedControl.caption : CollectiveRig.RegionNames[selectedRegion];

        // ===== Runtime caches and session state =====
        // Interaction configuration.
        private InteractionProfile profile;
        // Single-hand pinch state machine.
        private readonly CollectiveGestureSession gesture = new CollectiveGestureSession();
        // Bimanual span state machine.
        private readonly CollectiveBimanualSession bimanual = new CollectiveBimanualSession();
        // Most recently selected parameter control, used as the bimanual target.
        private CollectiveParameterControl selectedControl;
        // Whether a bimanual target exists and whether bimanual recovery is active.
        private bool bimanualSelected, bimanualRecovery;
        // Parameter baseline at bimanual editing onset.
        private float bimanualStartValue;
        // Cache of all dashboard parameter controls.
        private CollectiveParameterControl[] controls;
        // Control currently being dragged by one hand.
        private CollectiveParameterControl activeControl;
        // Whether a single-hand drag is active.
        private bool dragging;
        // Previous secondary (left hand/trigger) state for edge detection.
        private bool leftWasDown;
        // Previous apply state for one-shot press activation.
        private bool applyWasDown;
        // Previous undo state for edge detection.
        private bool undoWasDown;
        // Previous toggleView state for edge detection.
        private bool viewWasDown;
        // Previous compare state for hold-to-compare and release-to-restore.
        private bool compareWasDown;
        // Whether the application has interactive focus.
        private bool focused = true;
        // Initial drag height/value and status-update throttle time.
        private float dragStartHeight, dragStartValue, nextStatus;
        // Indoor model anchor and rotation.
        private Vector3 roomAnchor;
        private Quaternion roomRotation;
        // Target marker and transition fade plane.
        private Transform reticle, fadeQuad;
        // Pointer-ray renderer.
        private LineRenderer pointerLine;
        // Fade material instance.
        private Material fadeMaterial;
        // Indoor cabin navigation history for backward travel.
        private readonly Stack<int> roomHistory = new Stack<int>();
        // Neighbour cycling index per node, used by forward CycleNode.
        private int[] nextPort;
        // User feedback text.
        private string feedback = "Point at a parameter or module. Pinch, then move up or down.";
        private void Start()
        {
            // Set a fixed target frame rate and disable VSync to avoid additional interaction latency.
            Application.targetFrameRate = 72; QualitySettings.vSyncCount = 0;

            // Validate essential references; missing core components prevent safe operation.
            if (rig == null || !rig.Ready || inputReader == null || stateController == null || study == null || linkedViews == null || head == null || trackingOrigin == null)
                throw new InvalidOperationException("Interactive scene references are incomplete.");

            // Read interaction scale parameters from configuration.
            profile = stateController.Profile;
            handTravel = profile.handTravelMetres;
            overviewScale = profile.overviewScale;

            // Place font rendering in the foreground UI queue to avoid occlusion.
            font.material.renderQueue = 3510;

            // Initialize neighbour cycling indices.
            nextPort = new int[rig.data.nodes.Length];

            // Create the menu at runtime if absent from the scene.
            if (menu == null)
                CreateMenu();

            // Rebind buttons to interaction settings and cache parameter controls.
            RefreshMenuBindings();
            controls = menu.GetComponentsInChildren<CollectiveParameterControl>(true);

            // Assign references in both directions.
            dashboard.experience = this;
            rehearsal.experience = this;

            // Configure linked-view dependencies.
            linkedViews.rig = rig;
            linkedViews.head = head;
            linkedViews.trackingOrigin = trackingOrigin;
            linkedViews.panelRoot = menu;
            linkedViews.dashboard = dashboard;
            linkedViews.marsSky = marsSky;
            linkedViews.Initialize();
            dashboard.Initialize();

            // Initialize room-scale hints.
            if (roomScale == null)
            {
                roomScale = gameObject.AddComponent<CollectiveRoomScale>();
            }
            roomScale.Initialize(this);

            // Use the participant read-only workspace on headset, Android or direct spatial mode.
            if (directSpatialInteraction || XRSettings.isDeviceActive || Application.platform == RuntimePlatform.Android)
                dashboard.ConfigureHeadsetLayout();

            // Enable coupling by default in direct spatial mode.
            if (directSpatialInteraction) rig.coupled = true;

            // Ensure the main camera has a Skybox component using the Mars exterior material.
            var exterior = head.GetComponent<Skybox>();
            if (exterior == null)
                exterior = head.gameObject.AddComponent<Skybox>();
            exterior.material = marsSky;
            head.clearFlags = CameraClearFlags.Skybox;

            // Create the view-transition fade layer.
            CreateFade();

            // Create the target marker/crosshair.
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Selection reticle";
            Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = handMaterial;
            reticle = marker.transform; reticle.localScale = Vector3.one * .009f;

            // Create the hand/controller pointer line.
            var rayObject = new GameObject("Hand or controller pointer");
            pointerLine = rayObject.AddComponent<LineRenderer>();
            pointerLine.sharedMaterial = handMaterial;
            pointerLine.widthMultiplier = .002f;
            pointerLine.positionCount = 2;

            // Mark the current state as applied and initialize placement and display.
            stateController.Commit();
            PlaceOverview(); PlaceMenu();
            RenderSettings.skybox = marsSky;
            rig.Rebuild();
            RefreshStatus();

            // Start the initial cabin transition asynchronously.
            StartCoroutine(BeginExperience());
        }
        private IEnumerator BeginExperience()
        {
            // Switch to the reference cabin specified by StudyProfile.
            yield return ChangeView(true, rig.FindNode(study.Profile.referenceRoom));

            // Calibrate when the first post-transition pose is available for maximum stability.
            roomScale.Calibrate(); // First tracked pose is available after the opening transition.

            // Log startup status.
            Debug.Log("[SpatialCollective] Start ready: inside=" + rig.inside + ", room=" + rig.data.nodes[rig.selectedNode].id + ", coupled=" + rig.coupled + ", threeViews=" + dashboard.ParticipantViewsVisible);
        }
        private void Update()
        {
            // Skip until dependencies are ready.
            if (rig == null || !rig.Ready || profile == null) return;

            // Process input only when enabled and focused.
            if (ProcessHardwareInput && focused)
            {
                // Handle keyboard shortcuts first.
                KeyboardInput();

                // Read the unified input frame.
                CollectiveInputFrame frame = inputReader.Read();

                // Prefer the input frame's pointer; otherwise use a head-forward ray.
                Ray ray = frame.hasPointer ? frame.pointer : new Ray(head.transform.position, head.transform.forward);

                // Mouse input uses a screen-point ray.
                if (frame.source == PointerSource.Mouse && Mouse.current != null)
                    ray = head.ScreenPointToRay(Mouse.current.position.ReadValue());

                // Dispatch input.
                ProcessInput(frame, ray);
            }

            // Throttle status text refreshes.
            if (Time.unscaledTime >= nextStatus)
            {
                nextStatus = Time.unscaledTime + 0.08f;
                RefreshStatus();
            }
        }
        public void ProcessInput(CollectiveInputFrame frame, Ray ray)
        {
            // Route hand input to the spatial state machine in direct spatial mode.
            if (directSpatialInteraction && (frame.source == PointerSource.Hand || frame.source == PointerSource.HandGaze || frame.source == PointerSource.None))
            {
                ProcessSpatialInput(frame, ray, Time.unscaledDeltaTime);
                return;
            }

            // Reposition the menu when recalled by the open left palm.
            if (frame.recall) PlaceMenu();

            // Bimanual recovery requires a release before the next edit.
            if (bimanualRecovery)
            {
                // Hand input recovers when both hands are tracked and released.
                if (frame.leftTracked && frame.rightTracked && !frame.primary && !frame.secondary) bimanualRecovery = false;
                // Controller/mouse input recovers when primary and secondary buttons are released.
                else if ((frame.source == PointerSource.Controller || frame.source == PointerSource.Mouse) && !frame.primary && !frame.secondary)
                {
                    bimanualRecovery = false;
                    bimanual.Suspend();
                    gesture.Suspend();
                }
                // Block subsequent processing until recovery completes.
                else return;
            }

            // Update bimanual session state.
            bimanual.Step(frame, handTravel);

            // On bimanual end, freeze the preview and enter recovery.
            if (bimanual.Ended)
            {
                dragging = false;
                activeControl = null;
                bimanualRecovery = true;
                gesture.Suspend();
                rig.Rebuild();
                feedback = "PREVIEW HELD. Open both hands before editing again; APPLY confirms."; return;
            }

            // Bimanual editing requires an existing target and no compare or transition state.
            if (bimanual.Began)
            {
                if (!bimanualSelected || stateController.IsComparing || IsTransitioning)
                {
                    bimanual.Suspend();
                    bimanualRecovery = true;
                    feedback = "First select a parameter with the right hand. Then open both hands.";
                    return;
                }

                // Before bimanual editing, stop demonstrations and automatic flight and clear single-hand drag state.
                study.PauseForEdit();
                rig.animateDetached = false;
                dragging = false;
                activeControl = null;
                gesture.Suspend();

                // Capture the bimanual parameter baseline.
                bimanualStartValue = selectedControl != null ? selectedControl.Read(rig) : rig.GetBaseline(selectedRegion);
            }

            // During bimanual editing, update the value from span changes.
            if (bimanual.Active)
            {
                float range = selectedControl != null ? selectedControl.Maximum - selectedControl.Minimum : 1;
                float value = bimanualStartValue + bimanual.Delta * range;
                if (selectedControl != null)
                    selectedControl.Write(rig, value);
                else
                    rig.SetBaseline(selectedRegion, value);
                rig.MarkDirty();
                feedback = "TWO HANDS: spread / close to adjust the selected JSON parameter.";
                dashboard.ObserveInput(frame, ray.GetPoint(1.2f)); return;
            }

            // Update the single-hand session.
            gesture.Step(frame);

            // Reset button edges when input source changes to avoid accidental reconnect activation.
            if (gesture.SourceChanged)
            {
                // Do not replay held controller buttons when a device reconnects.
                applyWasDown = frame.apply; undoWasDown = frame.undo;
                viewWasDown = frame.toggleView; compareWasDown = frame.compare;
            }

            // Freeze the preview on session interruptions such as tracking changes.
            if (gesture.Interrupted)
            {
                dragging = false;
                activeControl = null;
                study.Pause();
                rig.animateDetached = false;
                feedback = "Tracking changed. Preview is frozen. Open your hand, then pinch again.";
            }

            // The left controller's secondary press recentres the menu outside hand-tracking input.
            if (frame.secondary && !leftWasDown && !frame.primary && !frame.leftTracked) PlaceMenu();

            // Remember the left secondary-button state.
            leftWasDown = frame.secondary;

            // Handle Apply, Undo and ToggleView press edges.
            if (frame.apply && !applyWasDown) 
                Execute(CollectiveCommand.Apply, 0);
            if (frame.undo && !undoWasDown) 
                Execute(CollectiveCommand.Undo, 0);
            if (frame.toggleView && !viewWasDown) 
                Execute(CollectiveCommand.ToggleView, 0);

            // Compare uses hold/release state transitions rather than a single press edge.
            if (frame.compare != compareWasDown)
            {
                if (frame.compare)
                {
                    StopMotion();
                    stateController.BeginCompare();
                }
                else
                    stateController.EndCompare();
            }

            // Update cached button states.
            applyWasDown = frame.apply; undoWasDown = frame.undo; viewWasDown = frame.toggleView; compareWasDown = frame.compare;

            // Raycast against UI layer 9.
            bool hit = Physics.Raycast(ray, out RaycastHit result, 12, 1 << 9, QueryTriggerInteraction.Collide);

            // Resolve components on the hit object.
            var button = hit ? result.collider.GetComponent<CollectiveButton>() : null;
            var parameter = hit ? result.collider.GetComponent<CollectiveParameterControl>() : null;
            var dashboardAction = hit ? result.collider.GetComponent<CollectiveDashboardAction>() : null;
            var viewport = hit ? result.collider.GetComponent<CollectiveViewportTarget>() : null;

            // For overview hits, project into the model before picking; other UI hits block model selection.
            int node = viewport != null && viewport.sourceCamera != null ? PickNode(viewport.ModelRay(result.point)) : hit ? -1 : PickNode(ray, true);

            // Place the marker at the UI hit, then node centre, or a default point along the ray.
            if (reticle != null) reticle.position = hit ? result.point - ray.direction * .009f : node >= 0 ? rig.transform.TransformPoint(rig.NodeCenter(node)) : ray.GetPoint(1.2f);

            // Update pointer-line visibility.
            if (pointerLine != null)
            {
                pointerLine.enabled = frame.hasPointer;
                pointerLine.SetPosition(0, ray.origin); pointerLine.SetPosition(1, reticle.position);
            }

            // Reject interaction during transitions.
            if (IsTransitioning) return;

            // On a single-hand pinch press, click, select or prepare dragging.
            if (gesture.Began)
            {
                // Internal dashboard action.
                if (dashboardAction != null)
                {
                    StopMotion();
                    dashboard.HandleAction(dashboardAction.action);
                    dashboard.ObserveInput(frame, result.point);
                    return;
                }
                // Standard command button.
                if (button != null)
                {
                    Execute(button.action, button.argument);
                    dashboard.ObserveInput(frame, result.point);
                    return;
                }
                // Disable editing in Compare mode.
                if (stateController.IsComparing)
                {
                    feedback = "Viewing applied state. Exit COMPARE to edit.";
                    return;
                }

                // Prepare dragging when a parameter control or model node is hit.
                if (parameter != null || node >= 0)
                {
                    // Stop demonstrations and previous drags before editing.
                    StopMotion();

                    // Remember the current drag target.
                    activeControl = parameter;
                    selectedControl = parameter; bimanualSelected = true;

                    // Selection behaviour depends on parameter control type.
                    if (parameter != null && parameter.parameter == CollectiveParameter.Activation) SelectRegion(parameter.index, true);
                    if (parameter != null && parameter.parameter == CollectiveParameter.Travel) InspectionNode = rig.NodeForRegion(parameter.index + 4);
                    if (parameter != null && parameter.parameter == CollectiveParameter.Coupling) InspectionNode = rig.NodeForRegion(parameter.index + 1);

                    // For model-node hits, derive the region and tab from the node.
                    if (parameter == null) { selectedRegion = rig.RegionForNode(node); dashboard.ShowTab(selectedRegion < 4 ? 0 : selectedRegion - 3); InspectionNode = node; if (!rig.inside) { rig.selectedNode = node; rig.MarkDirty(); } }

                    // Capture the initial value and hand height.
                    dragStartValue = parameter != null ? parameter.Read(rig) : rig.GetBaseline(selectedRegion);
                    dragStartHeight = frame.height;
                    dragging = true;

                    // Highlight the region and indicate that dragging is available.
                    rig.Highlight(selectedRegion); feedback = "PREVIEW: move the pinched hand up / down. Release to hold.";
                }
                // Show feedback when no target is hit.
                else feedback = "No target selected. Point at a module or parameter first.";
            }

            // Map vertical hand displacement linearly to the parameter during single-hand dragging.
            if (dragging && gesture.Held)
            {
                float delta = frame.height - dragStartHeight;
                // Small-motion jitter dead zone.
                if (Mathf.Abs(delta) < .003f) delta = 0;
                float range = activeControl != null ? activeControl.Maximum - activeControl.Minimum : 1;
                float value = dragStartValue + delta / handTravel * range;
                if (activeControl != null)
                    activeControl.Write(rig, value);
                else
                    rig.SetBaseline(selectedRegion, value);
                rig.MarkDirty();
            }

            // On release, retain the preview pending Apply or Undo.
            if (dragging && gesture.Released)
            {
                dragging = false;
                activeControl = null; rig.Rebuild();
                feedback = "PREVIEW HELD. APPLY to confirm, UNDO to restore, COMPARE to inspect.";
            }

            // Update Dashboard input-observation state.
            dashboard.ObserveInput(frame, hit ? result.point : reticle.position);
        }
        public int PickNode(Ray ray, bool respectLocalVisibility = false)
        {
            // Analytic picking volumes follow actual deformed centres, including the two flying units.
            // Initialize the result and nearest intersection distance.
            int result = -1; float nearest = float.PositiveInfinity, s = rig.transform.lossyScale.x;

            // Test every node with analytic ray-sphere intersection.
            for (int n = 0; n < rig.data.nodes.Length; n++)
            {
                // Respect indoor camera visibility by skipping filtered nodes when requested.
                if (respectLocalVisibility && rig.inside && head.GetComponent<CollectiveViewFilter>().Weight(n) <= 0) continue;

                // Compute the world centre and vector from ray origin to centre.
                Vector3 center = rig.transform.TransformPoint(rig.NodeCenter(n)), oc = ray.origin - center;

                // Choose the picking-sphere radius by node type.
                float radius = (rig.data.nodes[n].kind == "S" ? 4.8f : rig.data.nodes[n].kind == "O" ? 3.4f : 2.6f) * s;

                // Ray-sphere discriminant.
                float b = Vector3.Dot(oc, ray.direction), c = oc.sqrMagnitude - radius * radius, d = b * b - c;
                if (d < 0) continue;

                // Choose the nearest positive intersection distance.
                float t = -b - Mathf.Sqrt(d); if (t < 0) t = -b + Mathf.Sqrt(d);

                // Update the nearest hit.
                if (t >= 0 && t < nearest) { nearest = t; result = n; }
            }

            // Return the hit node, or -1 if none.
            return result;
        }
        private void LateUpdate()
        {
            if (rig != null && rig.Ready && rig.inside)
                rig.transform.position = roomAnchor - roomRotation * (rig.NodeFloor(rig.selectedNode) * inhabitedScale);
        }
        private void SelectRegion(int region, bool changeRoom)
        {
            // Update the selected region.
            selectedRegion = region;

            // Update the linked-view inspection node.
            InspectionNode = rig.NodeForRegion(region);

            // Clear control-specific targeting and mark the bimanual target ready.
            selectedControl = null; bimanualSelected = true;

            // In overview, optionally update the next entry node.
            if (changeRoom && !rig.inside) { rig.selectedNode = rig.NodeForRegion(region); rig.MarkDirty(); }

            // Highlight the selected region wireframe.
            rig.Highlight(region);
        }
        public void InspectRegion(int region) => InspectionNode = rig.NodeForRegion(region);
        private void StopMotion()
        {
            study.PauseForEdit();
            rig.animateDetached = false;
            dragging = false;
            activeControl = null;
        }
        public void Execute(CollectiveCommand action, int arg)
        {
            // Validate command and argument first.
            InteractionProfile.ValidateCommand(action.ToString(), arg);

            // Block commands during transitions.
            if (IsTransitioning) return;

            // Cancel bimanual and single-hand drag sessions before execution.
            bimanual.Suspend();
            dragging = false; activeControl = null;

            // Exit Compare before commands other than ToggleCompare itself.
            if (action != CollectiveCommand.ToggleCompare && stateController.IsComparing) stateController.EndCompare();

            // Dispatch commands.
            switch (action)
            {
                case CollectiveCommand.SelectRegion: SelectRegion(arg, true); feedback = CollectiveRig.RegionNames[arg] + " selected."; break;
                case CollectiveCommand.Preset: StopMotion(); stateController.ApplyPreset(arg); feedback = "Preset preview. APPLY to confirm."; break;
                case CollectiveCommand.ToggleView:
                    // Clear route history before switching views.
                    roomHistory.Clear();

                    // Prefer the inspection target in direct spatial mode.
                    int destination = directSpatialInteraction && InspectionNode >= 0 ? InspectionNode : rig.selectedNode;
                    
                    // Enter indoors from overview or when targeting another node; otherwise return to overview.
                    bool enter = !rig.inside || destination != rig.selectedNode;
                    StartCoroutine(ChangeView(enter, destination)); break;
                case CollectiveCommand.SelectReferenceRoom: roomHistory.Clear(); StartCoroutine(ChangeView(true, rig.FindNode(study.Profile.referenceRoom))); break;
                case CollectiveCommand.CycleNode: CycleNode(arg); break;
                case CollectiveCommand.ToggleCoupling: StopMotion(); rig.coupled = !rig.coupled; rig.MarkDirty(); feedback = rig.coupled ? "LINKED: signed responses follow CORE." : "FREE: all six zones are independent."; break;
                case CollectiveCommand.ToggleRooms: rig.showAllChambers = !rig.showAllChambers; rig.MarkDirty(); break;
                case CollectiveCommand.Apply:
                    StopMotion();
                    rig.Rebuild();
                    stateController.Commit();
                    feedback = "APPLIED. Shape, link responses and travel positions confirmed.";
                    break;
                case CollectiveCommand.Undo:
                    StopMotion();
                    stateController.Undo();
                    feedback = "Restored the last applied configuration.";
                    break;
                case CollectiveCommand.ToggleCompare:
                    StopMotion();
                    if (stateController.IsComparing)
                        stateController.EndCompare();
                    else
                        stateController.BeginCompare();
                    feedback = stateController.IsComparing ? "APPLIED STATE. COMPARE again to return to your preview." : "Returned to preview."; break;
                case CollectiveCommand.ToggleDemo: 
                    rig.animateDetached = false;
                    study.Toggle(); 
                    feedback = study.Playing ? "32-second motion study. Manual editing interrupts playback." : "Study paused. This remains an unapplied preview."; break;
                case CollectiveCommand.RestartDemo: rig.animateDetached = false; study.Pause(); study.Seek(0); study.Toggle(); feedback = "Restarted: OBSERVE > UNFURL > DIFFERENTIATE > RETREAT > REACH."; break;
                case CollectiveCommand.ToggleDrift:
                    study.PauseForEdit();
                    if (!rig.animateDetached && rig.departureProgress >= 1 && rig.approachProgress >= 1) { rig.SetTravel(0, 0); rig.SetTravel(1, 0); }
                    rig.animateDetached = !rig.animateDetached; feedback = "Travel is independent of shape. Use S08 DEPART / O13 APPROACH bars."; break;
                case CollectiveCommand.Reposition: PlaceMenu(); linkedViews.Calibrate(); if (!rig.inside) PlaceOverview(); break;
                case CollectiveCommand.ToggleBackground: RenderSettings.skybox = directSpatialInteraction ? marsSky : RenderSettings.skybox == cabinSky ? marsSky : cabinSky; break;
                case CollectiveCommand.HideMenu: menu.gameObject.SetActive(false); break;
                case CollectiveCommand.SaveState:
                    try { stateController.SaveApplied(); feedback = "Applied configuration saved on this device."; } catch (Exception e) { feedback = "Could not save: " + e.Message; }
                    break;
                case CollectiveCommand.LoadState:
                    StopMotion();
                    try { stateController.LoadPreview(); feedback = "Saved configuration loaded as PREVIEW. APPLY to confirm."; } catch (Exception e) { feedback = "Could not load: " + e.Message; }
                    break;
                case CollectiveCommand.ImportAuthoredState:
                    StopMotion();
                    try
                    {
                        // An external authored JSON asset must be assigned in the scene.
                        if (stateController.authoredStateJson == null) throw new InvalidOperationException("Assign AuthoredState.json in the scene.");
                        // Load as a preview without committing immediately.
                        stateController.PreviewAuthoredJson(stateController.authoredStateJson.text);
                        feedback = "GH state previewed. Resolved values loaded in FREE mode; APPLY to confirm.";
                    }
                    catch (Exception e) { feedback = "Could not import: " + e.Message; }
                    break;
            }

            // Refresh the UI immediately after executing the command.
            RefreshStatus();
        }
        private void CycleNode(int direction)
        {
            // The next node to visit.
            int next; int[] neighbors = rig.data.nodes[rig.selectedNode].neighbors;

            // Interior mode: navigate forward or backward through the adjacency graph.
            if (rig.inside)
            {
                // Backward: pop the previous node from history.
                if (direction < 0) { if (roomHistory.Count == 0) { feedback = "No previous room on this route."; return; } next = roomHistory.Pop(); }
                else
                {
                    // Detached cabins have no neighbors to visit.
                    if (neighbors.Length == 0) { feedback = "Detached unit: no connected room. EXIT returns to the collective."; return; }
                    // Cycle through neighbors and push the current node onto the history stack.
                    next = neighbors[nextPort[rig.selectedNode]++ % neighbors.Length]; roomHistory.Push(rig.selectedNode);
                }
            }
            // Overview mode: cycle through node indices.
            else next = (rig.selectedNode + direction + rig.data.nodes.Length) % rig.data.nodes.Length;

            // Start the transition.
            StartCoroutine(ChangeView(rig.inside, next));
        }
        private IEnumerator ChangeView(bool inside, int node)
        {
            // Ignore invalid nodes and requests during an active transition.
            if (IsTransitioning || node < 0 || node >= rig.data.nodes.Length) yield break;

            // Lock the transition state and stop interaction editing.
            IsTransitioning = true; StopMotion(); gesture.Suspend();

            // Fade to black.
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime) { fadeMaterial.color = new Color(0, 0, 0, t / .18f); yield return null; }
            fadeMaterial.color = Color.black;

            // Apply the destination view state.
            rig.inside = inside; rig.selectedNode = node; selectedRegion = rig.RegionForNode(node);
            InspectionNode = node; selectedControl = null; bimanualSelected = true;

            // Rebuild the mapping from body orientation to cabin orientation when entering an interior.
            if (inside)
            {
                Vector3 direction = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
                int[] neighbors = rig.data.nodes[node].neighbors;
                Vector3 local = neighbors.Length > 0 ? rig.NodeCenter(neighbors[0]) - rig.NodeCenter(node) : rig.data.overviewCenter - rig.NodeCenter(node);
                local = Vector3.ProjectOnPlane(local, Vector3.up); if (local.sqrMagnitude < .01f) local = Vector3.forward;
                roomRotation = Quaternion.LookRotation(direction) * Quaternion.Inverse(Quaternion.LookRotation(local));
                roomAnchor = new Vector3(head.transform.position.x, trackingOrigin.position.y, head.transform.position.z);
                rig.transform.localScale = Vector3.one * inhabitedScale; rig.transform.rotation = roomRotation;
                rig.transform.position = roomAnchor - roomRotation * (rig.NodeFloor(node) * inhabitedScale);
            }
            // Reposition the model when entering overview mode.
            else PlaceOverview();

            // The imported, deformable cabin supplies the interior. A photographed
            // interior behind it would duplicate walls and cannot follow body motion.

            // Use the same exterior skybox for interior and overview views.
            RenderSettings.skybox = marsSky;

            // Update geometry, reposition the menu, and recalibrate the linked views' reference pose.
            rig.Rebuild(); PlaceMenu(); linkedViews.Calibrate();

            // Update the interaction hint.
            feedback = inside ? "BODY 1:1 / enlarged habitat. Pinch space to reshape." : "Point and pinch a 3D module. Two hands change shape; one hand moves a detached unit.";

            // Fade in from black.
            for (float t = 0; t < .18f; t += Time.unscaledDeltaTime) { fadeMaterial.color = new Color(0, 0, 0, 1 - t / .18f); yield return null; }

            // Release the transition lock.
            fadeMaterial.color = Color.clear; IsTransitioning = false;

            // Log the completed transition.
            Debug.Log("[SpatialCollective] View ready: inside=" + rig.inside + ", room=" + rig.data.nodes[rig.selectedNode].id);
        }
        private void PlaceOverview()
        {
            // Use the head's horizontal forward direction to orient the overview.
            Vector3 f = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized; if (f.sqrMagnitude < .01f) f = Vector3.forward;
            Quaternion q = Quaternion.LookRotation(f, Vector3.up);

            // Apply the overview scale and rotation.
            rig.transform.rotation = q; rig.transform.localScale = Vector3.one * overviewScale;

            // Align overviewCenter with a point 2.2 m ahead of the head and 0.15 m higher.
            rig.transform.position = new Vector3(head.transform.position.x, head.transform.position.y + .15f, head.transform.position.z) + f * 2.2f - q * (rig.data.overviewCenter * overviewScale);
        }
        public void PlaceMenu()
        {
            // Ensure that the menu is visible.
            menu.gameObject.SetActive(true);

            // Reset the headset layout through the Dashboard's dedicated logic.
            if (HeadsetLayout) { dashboard.RecenterWorkspace(); return; }

            // Keep the desktop engineering panel facing the user's horizontal forward direction.
            Vector3 f = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized; if (f.sqrMagnitude < .01f) f = Vector3.forward;
            menu.rotation = Quaternion.LookRotation(f, Vector3.up);

            // Compensate for the field of view to maintain a consistent apparent size.
            float desktopScale = Mathf.Tan(head.fieldOfView * Mathf.Deg2Rad * .5f) / Mathf.Tan(38.3f * Mathf.Deg2Rad * .5f);
            menu.localScale = Vector3.one * (HeadsetLayout ? 1 : desktopScale);

            // Place the panel at a fixed distance in front of the head.
            menu.position = head.transform.position + f * (HeadsetLayout ? 1.5f : 2.8f);
        }
        private void RefreshStatus()
        {
            // Skip until both Dashboard and Rig are ready.
            if (dashboard == null || !rig.Ready) return;

            // Refresh the main panel.
            dashboard.Refresh();

            // Refresh the display of each parameter control.
            if (controls != null) foreach (var c in controls) c.Refresh(rig, selectedRegion);
        }
        private void KeyboardInput()
        {
            // Return if no keyboard is available.
            var k = Keyboard.current; if (k == null) return;

            // Execute command bindings from the configuration file.
            foreach (var binding in profile.keyboard) if (k[(Key)Enum.Parse(typeof(Key), binding.key)].wasPressedThisFrame) Execute((CollectiveCommand)Enum.Parse(typeof(CollectiveCommand), binding.command), binding.argument);

            // Allow arrow keys to adjust the current region's baseline outside compare mode.
            if (!stateController.IsComparing && (k.upArrowKey.isPressed || k.downArrowKey.isPressed))
            { 
                StopMotion(); 
                rig.SetBaseline(selectedRegion, 
                    rig.GetBaseline(selectedRegion) + Time.unscaledDeltaTime * .3f * (k.upArrowKey.isPressed ? 1 : -1)); 
            }

            // Desktop mode: use the right mouse button to look around and WASD to move horizontally.
            if (!XRSettings.isDeviceActive)
            {
                if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                { Vector2 d = Mouse.current.delta.ReadValue() * .12f; Vector3 e = head.transform.localEulerAngles; head.transform.localRotation = Quaternion.Euler(e.x - d.y, e.y + d.x, 0); }
                Vector3 move = Vector3.zero;
                if (k.wKey.isPressed) move += head.transform.forward; if (k.sKey.isPressed) move -= head.transform.forward;
                if (k.aKey.isPressed) move -= head.transform.right; if (k.dKey.isPressed) move += head.transform.right;
                trackingOrigin.position += Vector3.ProjectOnPlane(move, Vector3.up) * Time.unscaledDeltaTime * .65f;
            }
        }
        public void CreateMenu()
        {
            // Read the interaction configuration.
            profile = InteractionProfile.Load(stateController.interactionJson);

            // Create the menu root.
            menu = new GameObject("World-anchored interaction panel").transform;

            // Attach and build the Dashboard.
            dashboard = menu.gameObject.AddComponent<CollectiveDashboard>();
            dashboard.experience = this;
            dashboard.Build(profile);
        }
        public void RefreshMenuBindings()
        {
            // Reload the configuration to match the current JSON at runtime.
            profile = InteractionProfile.Load(stateController.interactionJson);

            // Get all button components.
            var buttons = menu.GetComponentsInChildren<CollectiveButton>(true);

            // Report an error if the button count does not match the configuration.
            if (buttons.Length != profile.menu.Length) throw new InvalidOperationException("Saved menu does not match Interaction.json.");

            // Track duplicate bindingIndex values.
            var used = new HashSet<int>();
            foreach (var button in buttons)
            {
                // Binding indices must be valid and unique.
                if (button.bindingIndex < 0 || button.bindingIndex >= profile.menu.Length || !used.Add(button.bindingIndex)) throw new InvalidOperationException("Invalid or duplicate menu binding.");

                // Write back the command and display label.
                var binding = profile.menu[button.bindingIndex]; button.action = binding.ParsedCommand; button.argument = binding.argument; button.label.text = binding.label;
            }
        }
        private void CreateFade()
        {
            // Create a Quad as the full-screen fade layer.
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(go.GetComponent<Collider>());

            // Attach it a short distance in front of the head camera.
            go.name = "View-change fade";
            fadeQuad = go.transform; fadeQuad.SetParent(head.transform, false);
            fadeQuad.localPosition = new Vector3(0, 0, .11f);
            fadeQuad.localScale = Vector3.one;

            // Clone the UI material and use a high render queue.
            fadeMaterial = new Material(uiMaterial);
            fadeMaterial.renderQueue = 4000;
            fadeMaterial.color = Color.clear;
            go.GetComponent<Renderer>().sharedMaterial = fadeMaterial;
        }
        public void SuspendInteraction()
        {
            // Stop any active rehearsal and restore the user's state.
            if (rehearsal != null && rehearsal.Playing) rehearsal.Stop();

            // Cancel spatial editing and roll back unfinished changes.
            CancelSpatialEdit();

            // Clear one-hand and two-hand interaction states.
            dragging = false; activeControl = null; gesture.Suspend();
            bimanual.Suspend(); bimanualRecovery = false;

            // Pause the demonstration and stop automatic detached-cabin flight.
            if (study != null) study.Pause(); if (rig != null) rig.animateDetached = false;

            // Mark button edges as already pressed to prevent retriggering immediately on resume.
            applyWasDown = undoWasDown = viewWasDown = compareWasDown = leftWasDown = true;

            // Update the hint.
            feedback = "Interaction paused. Open your hand before continuing.";
        }
        private void OnApplicationFocus(bool value) { focused = value || ((Application.isEditor || Application.platform == RuntimePlatform.WindowsPlayer) && XRSettings.isDeviceActive); if (!focused) SuspendInteraction(); }
        private void OnApplicationPause(bool value) { if (value) SuspendInteraction(); }
        private void OnDisable() { SuspendInteraction(); }
        private void OnDestroy()
        {
            // Destroy the selection marker and pointer line objects.
            if (reticle != null) Destroy(reticle.gameObject); if (pointerLine != null) Destroy(pointerLine.gameObject);

            // Destroy the fade object and material instance.
            if (fadeQuad != null) Destroy(fadeQuad.gameObject); if (fadeMaterial != null) Destroy(fadeMaterial);
        }
    }
}
