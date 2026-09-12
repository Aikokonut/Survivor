using UnityEngine;

namespace Stickman
{
    public class YDepthModule : MonoBehaviour
    {
        [SerializeField] private float baseZ = -1f;
        [SerializeField] private float scale = 0.001f;
        [SerializeField] private Transform depthAnchor;
        [SerializeField] private Renderer cachedRenderer;

        const string AnchorName = "DepthAnchor";

        public float ZFromY(float y) => baseZ + y * scale;

        public static float CalculateZ(float y, float bZ = -1f, float sc = 0.001f) => bZ + y * sc;

        public float ZForMover(float y) => ZFromY(y);

        public Vector3 With(Vector3 p) => new(p.x, p.y, ZFromY(p.y));

        public Vector3 WithMover(Vector2 p) => new(p.x, p.y, ZFromY(p.y));

        public Vector3 WithMover(Vector2 p, Transform mover) => new(p.x, p.y, ZFromY(p.y));

        private void Awake()
        {
            if (depthAnchor == null) depthAnchor = FindDepthAnchor(transform);
            if (cachedRenderer == null) TryGetComponent(out cachedRenderer);
        }

        public void Apply(Transform t)
        {
            if (t == null) return;
            Vector3 p = t.position;
            float z = ZFromY(DepthY(t));
            if (Mathf.Abs(p.z - z) > 1e-7f)
                t.position = new Vector3(p.x, p.y, z);
        }

        public float DepthY(Transform t)
        {
            if (depthAnchor != null) return depthAnchor.position.y;
            if (cachedRenderer != null) return cachedRenderer.bounds.min.y;
            return t != null ? t.position.y : 0f;
        }

        public static void ApplyWorld(Transform t)
        {
            if (t == null) return;
            Vector3 p = t.position;
            float z = CalculateZ(p.y);
            if (Mathf.Abs(p.z - z) > 1e-7f)
                t.position = new Vector3(p.x, p.y, z);
        }

        static Transform FindDepthAnchor(Transform root)
        {
            if (root == null) return null;
            if (root.name == AnchorName) return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDepthAnchor(root.GetChild(i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
