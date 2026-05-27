using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace ArenaCombat.Core.Skill
{
    [RequireComponent(typeof(NetworkObject))]
    public class SkillRangeDisplay : NetworkBehaviour
    {
        public static SkillRangeDisplay Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private bool  _showRange = true;
        [SerializeField] private bool  _logRange  = true;
        [SerializeField] private float _duration  = 2.0f;
        [SerializeField] private float _lineHalfWidth = 0.75f;

        [Header("Pool")]
        [SerializeField] private int _poolSize = 8;

        [Header("Runtime colors (Game view)")]
        [SerializeField] private Color _hitColor  = new Color(1f, 0.1f, 0.1f, 0.6f);
        [SerializeField] private Color _missColor = new Color(1f, 1f, 0f, 0.5f);
        [SerializeField] private Color _projColor = new Color(0.2f, 0.5f, 1f, 0.6f);
        [SerializeField] private Color _areaColor = new Color(0.8f, 0.2f, 1f, 0.5f);

        [Header("Gizmo colors (Scene view)")]
        [SerializeField] private Color _gizmoHitColor  = new Color(0f, 1f, 0f, 0.9f);
        [SerializeField] private Color _gizmoMissColor = new Color(0f, 1f, 1f, 0.9f);
        [SerializeField] private Color _gizmoProjColor = new Color(1f, 1f, 0f, 0.9f);
        [SerializeField] private Color _gizmoAreaColor = new Color(1f, 0.5f, 0f, 0.9f);
        [SerializeField] private bool  _showGizmos = true;
        [SerializeField] private int   _gizmoSegments = 32;

        private Material _sharedMat;
        private readonly Queue<PoolEntry> _pool = new();
        private readonly List<PoolEntry> _allEntries = new();
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        private class PoolEntry
        {
            public GameObject   Go;
            public MeshFilter   Filter;
            public MeshRenderer Renderer;
            public Mesh         Mesh;
            public Coroutine    ActiveFade;
        }

        // ── Gizmo record ────────────────────────────────────────
        private enum GizmoShape { Circle, Cone, Line, Area }

        private struct GizmoRecord
        {
            public Vector3    Center;
            public Vector3    Forward;
            public float      Radius;
            public float      AngleDeg;
            public GizmoShape Shape;
            public float      Timestamp;
            public bool       Hit;
        }

        private readonly List<GizmoRecord> _gizmoRecords = new();

        // ── Initialization ──────────────────────────────────────
        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;

            _sharedMat = CreateTransparentMaterial();

            for (int i = 0; i < _poolSize; i++)
                _pool.Enqueue(CreatePoolEntry());

            Debug.Log($"[SkillRangeDisplay] Init complete (pool size {_poolSize}, procedural mesh)");
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            foreach (var entry in _allEntries)
            {
                if (entry.Mesh != null) Destroy(entry.Mesh);
            }
            if (_sharedMat != null) Destroy(_sharedMat);
        }

        // ══════════════════════════════════════════════════════════
        // Runtime display (Game view)
        // ══════════════════════════════════════════════════════════

        public void ShowCircle(Vector3 center, float radius, bool hit)
        {
            RecordGizmo(center, Vector3.forward, radius, 360f, GizmoShape.Circle, hit);
            DisplayCircle(center, radius, hit);
            if (IsSpawned && IsServer) ShowCircleRpc(center, radius, hit);
        }

        public void ShowCone(Vector3 center, Vector3 forward, float radius, float angleDeg, bool hit)
        {
            RecordGizmo(center, forward, radius, angleDeg, GizmoShape.Cone, hit);
            DisplayCone(center, forward, radius, angleDeg, hit);
            if (IsSpawned && IsServer) ShowConeRpc(center, forward, radius, angleDeg, hit);
        }

        public void ShowLine(Vector3 origin, Vector3 forward, float length)
        {
            RecordGizmo(origin, forward, length, 0f, GizmoShape.Line, true);
            DisplayLine(origin, forward, length);
            if (IsSpawned && IsServer) ShowLineRpc(origin, forward, length);
        }

        public void ShowArea(Vector3 center, float radius)
        {
            RecordGizmo(center, Vector3.forward, radius, 360f, GizmoShape.Area, true);
            DisplayArea(center, radius);
            if (IsSpawned && IsServer) ShowAreaRpc(center, radius);
        }

        // ── Client replication RPCs ─────────────────────────────
        [Rpc(SendTo.NotServer)]
        private void ShowCircleRpc(Vector3 center, float radius, bool hit) => DisplayCircle(center, radius, hit);

        [Rpc(SendTo.NotServer)]
        private void ShowConeRpc(Vector3 center, Vector3 forward, float radius, float angleDeg, bool hit) => DisplayCone(center, forward, radius, angleDeg, hit);

        [Rpc(SendTo.NotServer)]
        private void ShowLineRpc(Vector3 origin, Vector3 forward, float length) => DisplayLine(origin, forward, length);

        [Rpc(SendTo.NotServer)]
        private void ShowAreaRpc(Vector3 center, float radius) => DisplayArea(center, radius);

        // ── Local display logic ─────────────────────────────────
        private void DisplayCircle(Vector3 center, float radius, bool hit)
        {
            if (!_showRange) return;
            if (_logRange) Debug.Log($"[RangeDisplay] Circle center={center} r={radius} hit={hit}");

            var entry = Acquire(center);
            BuildCircleMesh(entry.Mesh, radius);
            StartFade(entry, hit ? _hitColor : _missColor);
        }

        private void DisplayCone(Vector3 center, Vector3 forward, float radius, float angleDeg, bool hit)
        {
            if (!_showRange) return;
            if (_logRange) Debug.Log($"[RangeDisplay] Cone center={center} r={radius} angle={angleDeg} hit={hit}");

            var entry = Acquire(center);
            BuildConeMesh(entry.Mesh, forward.normalized, radius, angleDeg);
            StartFade(entry, hit ? _hitColor : _missColor);
        }

        private void DisplayLine(Vector3 origin, Vector3 forward, float length)
        {
            if (!_showRange) return;
            if (_logRange) Debug.Log($"[RangeDisplay] Line origin={origin} len={length}");

            var entry = Acquire(origin);
            BuildLineMesh(entry.Mesh, forward.normalized, length);
            StartFade(entry, _projColor);
        }

        private void DisplayArea(Vector3 center, float radius)
        {
            if (!_showRange) return;
            if (_logRange) Debug.Log($"[RangeDisplay] Area center={center} r={radius}");

            var entry = Acquire(center);
            BuildCircleMesh(entry.Mesh, radius);
            StartFade(entry, _areaColor);
        }

        // ══════════════════════════════════════════════════════════
        // Gizmo record + draw (Scene view)
        // ══════════════════════════════════════════════════════════

        private void RecordGizmo(Vector3 center, Vector3 forward, float radius,
                                 float angleDeg, GizmoShape shape, bool hit)
        {
            _gizmoRecords.Add(new GizmoRecord
            {
                Center    = center,
                Forward   = forward.normalized,
                Radius    = radius,
                AngleDeg  = angleDeg,
                Shape     = shape,
                Timestamp = Time.time,
                Hit       = hit,
            });
        }

        private void OnDrawGizmos()
        {
            if (!_showGizmos || _gizmoRecords.Count == 0) return;

            float now = Time.time;
            for (int i = _gizmoRecords.Count - 1; i >= 0; i--)
            {
                var rec = _gizmoRecords[i];
                float age = now - rec.Timestamp;
                if (age > _duration) { _gizmoRecords.RemoveAt(i); continue; }

                float alpha = age < _duration * 0.5f ? 1f : 1f - (age - _duration * 0.5f) / (_duration * 0.5f);

                Color baseColor;
                switch (rec.Shape)
                {
                    case GizmoShape.Line: baseColor = _gizmoProjColor; break;
                    case GizmoShape.Area: baseColor = _gizmoAreaColor; break;
                    default:              baseColor = rec.Hit ? _gizmoHitColor : _gizmoMissColor; break;
                }
                Gizmos.color = new Color(baseColor.r, baseColor.g, baseColor.b, baseColor.a * alpha);

                Vector3 center = new Vector3(rec.Center.x, 1.2f, rec.Center.z);

                switch (rec.Shape)
                {
                    case GizmoShape.Circle:
                    case GizmoShape.Area:
                        DrawWireCircle(center, rec.Radius);
                        break;
                    case GizmoShape.Cone:
                        DrawWireCone(center, rec.Forward, rec.Radius, rec.AngleDeg);
                        break;
                    case GizmoShape.Line:
                        DrawWireLine(center, rec.Forward, rec.Radius);
                        break;
                }
            }
        }

        private void DrawWireCircle(Vector3 center, float radius)
        {
            int seg = Mathf.Max(3, _gizmoSegments);
            Vector3 prev = center + new Vector3(radius, 0f, 0f);
            for (int i = 1; i <= seg; i++)
            {
                float angle = (float)i / seg * 360f * Mathf.Deg2Rad;
                Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        private void DrawWireCone(Vector3 center, Vector3 forward, float radius, float angleDeg)
        {
            float halfAngle = angleDeg * 0.5f;
            Quaternion leftRot  = Quaternion.Euler(0f, -halfAngle, 0f);
            Quaternion rightRot = Quaternion.Euler(0f,  halfAngle, 0f);

            Vector3 leftDir  = leftRot  * forward;
            Vector3 rightDir = rightRot * forward;

            Vector3 leftEnd  = center + leftDir  * radius;
            Vector3 rightEnd = center + rightDir * radius;

            Gizmos.DrawLine(center, leftEnd);
            Gizmos.DrawLine(center, rightEnd);

            int arcSegments = Mathf.Max(4, Mathf.RoundToInt(_gizmoSegments * angleDeg / 360f));
            Vector3 prev = leftEnd;
            for (int i = 1; i <= arcSegments; i++)
            {
                float t = (float)i / arcSegments;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
                Vector3 next = center + dir * radius;
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }

        private void DrawWireLine(Vector3 origin, Vector3 forward, float length)
        {
            float halfWidth = _lineHalfWidth;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized * halfWidth;
            Vector3 endPoint = origin + forward * length;

            Gizmos.DrawLine(origin + right, endPoint + right);
            Gizmos.DrawLine(origin - right, endPoint - right);
            Gizmos.DrawLine(origin + right, origin - right);
            Gizmos.DrawLine(endPoint + right, endPoint - right);
        }

        // ══════════════════════════════════════════════════════════
        // Mesh builders — same math as Gizmo wireframes
        // ══════════════════════════════════════════════════════════

        private void BuildCircleMesh(Mesh mesh, float radius)
        {
            mesh.Clear();
            int seg = Mathf.Max(3, _gizmoSegments);
            var verts = new Vector3[seg + 1];
            var tris  = new int[seg * 3];

            verts[0] = Vector3.zero;
            for (int i = 0; i < seg; i++)
            {
                float angle = (float)i / seg * 360f * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            for (int i = 0; i < seg; i++)
            {
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = (i + 1) % seg + 1;
                tris[i * 3 + 2] = i + 1;
            }

            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }

        private void BuildConeMesh(Mesh mesh, Vector3 forward, float radius, float angleDeg)
        {
            mesh.Clear();
            float halfAngle = angleDeg * 0.5f;
            int arcSeg = Mathf.Max(4, Mathf.RoundToInt(_gizmoSegments * angleDeg / 360f));

            var verts = new Vector3[arcSeg + 2];
            var tris  = new int[arcSeg * 3];

            verts[0] = Vector3.zero;
            for (int i = 0; i <= arcSeg; i++)
            {
                float t = (float)i / arcSeg;
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 dir = Quaternion.Euler(0f, angle, 0f) * forward;
                verts[i + 1] = dir * radius;
            }

            for (int i = 0; i < arcSeg; i++)
            {
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }

            mesh.vertices  = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
        }

        private void BuildLineMesh(Mesh mesh, Vector3 forward, float length)
        {
            mesh.Clear();
            float hw = _lineHalfWidth;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized * hw;
            Vector3 end   = forward * length;

            var verts = new Vector3[4];
            verts[0] = -right;
            verts[1] =  right;
            verts[2] = end + right;
            verts[3] = end - right;

            mesh.vertices  = verts;
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
        }

        // ══════════════════════════════════════════════════════════
        // Pool + Material
        // ══════════════════════════════════════════════════════════

        private PoolEntry Acquire(Vector3 worldCenter)
        {
            var entry = _pool.Count > 0 ? _pool.Dequeue() : CreatePoolEntry();

            if (entry.ActiveFade != null)
            {
                StopCoroutine(entry.ActiveFade);
                entry.ActiveFade = null;
            }

            entry.Go.SetActive(true);
            entry.Go.transform.position = new Vector3(worldCenter.x, 1.15f, worldCenter.z);
            entry.Go.transform.rotation = Quaternion.identity;
            return entry;
        }

        private void StartFade(PoolEntry entry, Color color)
        {
            entry.ActiveFade = StartCoroutine(FadeAndReturn(entry, color));
        }

        private IEnumerator FadeAndReturn(PoolEntry entry, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(ColorProp, color);
            entry.Renderer.SetPropertyBlock(block);

            float half = _duration * 0.5f;
            yield return new WaitForSeconds(half);

            float elapsed = 0f;
            while (elapsed < half)
            {
                elapsed += Time.deltaTime;
                float a = Mathf.Lerp(color.a, 0f, elapsed / half);
                block.SetColor(ColorProp, new Color(color.r, color.g, color.b, a));
                entry.Renderer.SetPropertyBlock(block);
                yield return null;
            }

            entry.Go.SetActive(false);
            entry.ActiveFade = null;
            _pool.Enqueue(entry);
        }

        private PoolEntry CreatePoolEntry()
        {
            var go = new GameObject("[RangeIndicator]");
            go.transform.SetParent(transform, false);
            go.SetActive(false);

            var filter   = go.AddComponent<MeshFilter>();
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _sharedMat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;

            var mesh = new Mesh { name = "SkillRange" };
            filter.mesh = mesh;

            var entry = new PoolEntry
            {
                Go       = go,
                Filter   = filter,
                Renderer = renderer,
                Mesh     = mesh,
            };
            _allEntries.Add(entry);
            return entry;
        }

        private static Material CreateTransparentMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Debug.LogWarning("[SkillRangeDisplay] URP Unlit shader not found, falling back to built-in Unlit/Color");
                shader = Shader.Find("Unlit/Color");
            }

            var mat = new Material(shader);
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetColor(ColorProp, Color.white);
            return mat;
        }
    }
}
