using UnityEngine;

namespace SpatialCollective
{
    /// <summary>Parameter type: regional deformation, coupling coefficient, or detached-cabin path progress.</summary>
    public enum CollectiveParameter { Activation, Coupling, Travel }
    /// <summary>Engineering-panel parameter control mapping its index to a model value and updating text and fill bars.</summary>
    public sealed class CollectiveParameterControl : MonoBehaviour
    {
        public CollectiveParameter parameter;
        public int index;
        public TextMesh label;
        public Transform fill;
        public string caption;
        public float width=.4f;
        public bool displayActual;
        public Color labelColor=new Color(.55f,.78f,.84f);
        public float Minimum => parameter == CollectiveParameter.Coupling ? -1 : 0;
        public float Maximum => 1;
        /// <summary>Read the corresponding model parameter; deformation uses its baseline value.</summary>
        /// <param name="rig">Model controller to read.</param>
        public float Read(CollectiveRig rig) => parameter == CollectiveParameter.Activation ? rig.GetBaseline(index) :
            parameter == CollectiveParameter.Coupling ? rig.GetCoupling(index) : rig.GetTravel(index);
        /// <summary>Write the corresponding parameter; setting path progress manually stops automatic flight.</summary>
        /// <param name="rig">Model controller to modify.</param>
        /// <param name="value">New parameter value, range-limited by the model controller.</param>
        public void Write(CollectiveRig rig, float value)
        {
            if(parameter == CollectiveParameter.Activation) rig.SetBaseline(index,value);
            else if(parameter == CollectiveParameter.Coupling) rig.SetCoupling(index,value);
            else {rig.animateDetached=false;rig.SetTravel(index,value);}
        }
        /// <summary>Refresh the numeric value, selection color, and fill length.</summary>
        /// <param name="rig">Model controller providing current deformation and path values.</param>
        /// <param name="selected">Selected region index used to highlight deformation controls.</param>
        public void Refresh(CollectiveRig rig, int selected)
        {
            float value=displayActual&&parameter==CollectiveParameter.Activation?rig.Actual[index]:Read(rig), amount=Mathf.InverseLerp(Minimum,Maximum,value);
            label.text=$"{caption}  {value:0.00}";
            label.color=parameter==CollectiveParameter.Activation && index==selected ? Color.white : labelColor;
            fill.localScale=new Vector3(Mathf.Max(.001f,amount)*width,.006f,.002f);
            fill.localPosition=new Vector3((amount-1)*width*.5f,-.026f,-.012f);
        }
    }
}
