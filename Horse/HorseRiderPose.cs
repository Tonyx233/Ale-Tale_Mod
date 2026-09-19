using UnityEngine;

namespace TonyMods
{
    // Change only the pelvis position and legs after animation/hand IK evaluation.
    [DefaultExecutionOrder(15000)]
    public sealed class HorseRiderPose : MonoBehaviour
    {
        private Animator animator;
        private Transform hips;
        private readonly Transform[] joints = new Transform[6];
        private readonly Quaternion[] rotations = new Quaternion[6];
        private readonly Transform[] toes = new Transform[2];
        private Vector3 hipsPosition;
        private bool applied, reportedMissing;
        private float nextBind;
        private PlayerNet player;
        public bool Riding;
        public TavernHorse Horse;

        private bool Bind()
        {
            if (Time.unscaledTime < nextBind) return false;
            nextBind = Time.unscaledTime + .5f;
            // The first Animator under PlayerNet can be a first-person weapon.
            var body = GetComponentInChildren<PlayerAnimTP>(true);
            animator = body == null ? null : body.GetComponentInChildren<Animator>(true);
            if (animator == null || !animator.isHuman || !animator.isActiveAndEnabled) return MissingRig();
            hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            HumanBodyBones[] names = { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot };
            for (int i = 0; i < joints.Length; i++)
            {
                joints[i] = animator.GetBoneTransform(names[i]);
                if (joints[i] == null) { hips = null; return MissingRig(); }
            }
            toes[0] = animator.GetBoneTransform(HumanBodyBones.LeftToes);
            toes[1] = animator.GetBoneTransform(HumanBodyBones.RightToes);
            player = GetComponent<PlayerNet>();
            if (hips == null || player == null) return MissingRig();
            reportedMissing = false;
            return true;
        }

        private bool MissingRig()
        {
            if (!reportedMissing)
            {
                Debug.LogWarning("Tony horse: waiting for the third-person humanoid leg rig; will retry.");
                reportedMissing = true;
            }
            return false;
        }

        private void Update() { Restore(); }
        private void LateUpdate()
        {
            if (!Riding || Horse == null) return;
            if (hips == null || animator == null || !animator.isActiveAndEnabled || player == null ||
                joints[0] == null || joints[1] == null || joints[2] == null ||
                joints[3] == null || joints[4] == null || joints[5] == null)
                if (!Bind()) return;
            Vector3 seat; Quaternion facing;
            if (!Horse.TryGetRiderSeat(player.OwnerClientId, out seat, out facing)) return;
            hipsPosition = hips.localPosition;
            for (int i = 0; i < joints.Length; i++) rotations[i] = joints[i].localRotation;
            applied = true;
            hips.position = seat;
            PoseLeg(0, -1, seat, facing);
            PoseLeg(3, 1, seat, facing);
        }

        private void PoseLeg(int index, float side, Vector3 seat, Quaternion facing)
        {
            Transform upper = joints[index], lower = joints[index + 1], foot = joints[index + 2];
            float a = Vector3.Distance(upper.position, lower.position);
            float b = Vector3.Distance(lower.position, foot.position);
            Vector3 target = seat + facing * new Vector3(side * .53f, -.65f, .02f);
            Vector3 delta = target - upper.position;
            HorseLegSolution solution;
            if (!HorsePoseMath.Solve(a, b, delta.magnitude, out solution)) return;
            Vector3 axis = delta.normalized;
            Vector3 hint = facing * new Vector3(side * .35f, .1f, 1f);
            Vector3 bend = Vector3.ProjectOnPlane(hint, axis).normalized;
            Vector3 knee = upper.position + axis * solution.Along + bend * solution.Height;
            target = upper.position + axis * solution.Distance;
            Aim(upper, lower.position - upper.position, knee - upper.position);
            Aim(lower, foot.position - lower.position, target - lower.position);
            Transform toe = toes[index / 3];
            if (toe != null) Aim(foot, toe.position - foot.position, facing * Vector3.forward);
        }

        private static void Aim(Transform bone, Vector3 from, Vector3 to)
        {
            if (from.sqrMagnitude > .000001f && to.sqrMagnitude > .000001f)
                bone.rotation = Quaternion.FromToRotation(from, to) * bone.rotation;
        }
        private void Restore()
        {
            if (!applied) return;
            if (hips != null) hips.localPosition = hipsPosition;
            for (int i = 0; i < joints.Length; i++) if (joints[i] != null) joints[i].localRotation = rotations[i];
            applied = false;
        }
        private void OnDisable() { Restore(); }
        private void OnDestroy() { Restore(); }
    }
}
