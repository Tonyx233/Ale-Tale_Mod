using System.Collections.Generic;
using UnityEngine;

namespace TonyMods
{
    // Applied after the native Animator, restored before the next animation evaluation.
    [DefaultExecutionOrder(15000)]
    public sealed class HorseRiderPose : MonoBehaviour
    {
        private Animator animator;
        private readonly List<Transform> joints = new List<Transform>();
        private readonly List<Quaternion> rotations = new List<Quaternion>();
        private readonly List<Vector3> bends = new List<Vector3>();
        private Transform hips;
        private Vector3 hipsPosition;
        private bool applied;
        public bool Riding;
        private void Awake()
        {
            foreach (Animator candidate in GetComponentsInChildren<Animator>(true))
                if (candidate.isHuman) { animator = candidate; break; }
            if (animator == null) return;
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            Add(HumanBodyBones.LeftUpperLeg, new Vector3(-70,0,-16));
            Add(HumanBodyBones.RightUpperLeg, new Vector3(-70,0,16));
            Add(HumanBodyBones.LeftLowerLeg,new Vector3(85,0,0));
            Add(HumanBodyBones.RightLowerLeg,new Vector3(85,0,0));
        }
        private void Add(HumanBodyBones bone, Vector3 bend)
        {
            Transform t = animator.GetBoneTransform(bone);
            if (t != null) { joints.Add(t); bends.Add(bend); rotations.Add(Quaternion.identity); }
        }
        private void Update() { Restore(); }
        private void LateUpdate()
        {
            if (!Riding || hips == null) return;
            hipsPosition = hips.localPosition;
            hips.position += Vector3.up * .72f;
            for (int i=0;i<joints.Count;i++) { rotations[i]=joints[i].localRotation; joints[i].localRotation*=Quaternion.Euler(bends[i]); }
            applied=true;
        }
        private void Restore()
        {
            if(!applied)return;
            if(hips!=null)hips.localPosition=hipsPosition;
            for(int i=0;i<joints.Count;i++)if(joints[i]!=null)joints[i].localRotation=rotations[i];
            applied=false;
        }
        private void OnDisable(){Restore();}
        private void OnDestroy(){Restore();}
    }
}
