using UnityEngine;

namespace SpatialCollective
{
    public enum CollectiveParameter { Activation, Coupling, Travel }
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
        public float Read(CollectiveRig rig) => parameter == CollectiveParameter.Activation ? rig.GetBaseline(index) :
            parameter == CollectiveParameter.Coupling ? rig.GetCoupling(index) : rig.GetTravel(index);
        public void Write(CollectiveRig rig, float value)
        {
            if(parameter == CollectiveParameter.Activation) rig.SetBaseline(index,value);
            else if(parameter == CollectiveParameter.Coupling) rig.SetCoupling(index,value);
            else {rig.animateDetached=false;rig.SetTravel(index,value);}
        }
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
