using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fieldsmith
{
    internal enum CellMark
    {
        Plant,
        Limited,
        Blocked,
    }

    // Ground markers drawn with pooled line renderers: one small ring per pattern cell and the pattern outline.
    // Points are only recomputed when the plan changes, not every frame.
    internal sealed class Preview
    {
        private const float Lift = 0.08f;
        private const int RingPoints = 12;
        private const int MaxOutlinePoints = 240;
        private const float OutlineSpacing = 0.5f;

        private static readonly Color PlantColor = new Color(0.55f, 1f, 0.45f, 0.95f);
        private static readonly Color LimitedColor = new Color(1f, 0.85f, 0.3f, 0.95f);
        private static readonly Color BlockedColor = new Color(1f, 0.35f, 0.3f, 0.95f);
        private static readonly Color OutlineColor = new Color(1f, 0.93f, 0.7f, 0.6f);
        private static readonly Color LockedOutlineColor = new Color(0.45f, 0.85f, 1f, 0.8f);

        private readonly GameObject root;
        private readonly Material material;
        private readonly List<LineRenderer> rings = new List<LineRenderer>();
        private readonly LineRenderer outline;
        private readonly Vector3[] buffer = new Vector3[MaxOutlinePoints];

        internal bool Available => material;

        internal Preview()
        {
            root = new GameObject("Fieldsmith_Preview");
            Object.DontDestroyOnLoad(root);
            material = CreateMaterial();
            outline = CreateLine("Outline", 0.05f, true);
            Hide();
        }

        internal void Destroy()
        {
            if (root) Object.Destroy(root);
            if (material) Object.Destroy(material);
        }

        internal void Hide()
        {
            if (root && root.activeSelf) root.SetActive(false);
        }

        // positions: planned plant positions (ground height); marks: what happens in each cell.
        internal void Show(List<Vector3> positions, List<CellMark> marks, GridFrame frame, int size, Vector3 aim, bool locked)
        {
            if (!root || !material) return;
            if (!root.activeSelf) root.SetActive(true);
            float radius = Mathf.Clamp((float)frame.Spacing * 0.3f, 0.08f, 0.35f);
            int count = Mathf.Min(positions.Count, marks.Count);
            while (rings.Count < count) rings.Add(CreateLine("Cell", 0.035f, true));
            for (int i = 0; i < rings.Count; i++)
            {
                LineRenderer ring = rings[i];
                if (i >= count)
                {
                    ring.positionCount = 0;
                    continue;
                }
                Color color = marks[i] == CellMark.Plant ? PlantColor : marks[i] == CellMark.Limited ? LimitedColor : BlockedColor;
                ring.startColor = color;
                ring.endColor = color;
                ring.positionCount = RingPoints;
                Vector3 center = positions[i];
                for (int k = 0; k < RingPoints; k++)
                {
                    float a = k * Mathf.PI * 2 / RingPoints;
                    float x = center.x + Mathf.Sin(a) * radius;
                    float z = center.z + Mathf.Cos(a) * radius;
                    float y = Ground(x, z, out float h) ? h : center.y;
                    ring.SetPosition(k, new Vector3(x, y + Lift, z));
                }
            }
            DrawOutline(frame, size, aim, locked);
        }

        // Rectangle half a spacing outside the outer cells, following the ground.
        private void DrawOutline(GridFrame frame, int size, Vector3 aim, bool locked)
        {
            GridMath.PatternStart(frame, aim.x, aim.z, size, out int startI, out int startJ);
            double i0 = startI - 0.5;
            double j0 = startJ - 0.5;
            double i1 = startI + size - 0.5;
            double j1 = startJ + size - 0.5;
            double[] ci = { i0, i1, i1, i0 };
            double[] cj = { j0, j0, j1, j1 };
            double sideLength = size * frame.Spacing;
            int perSide = Mathf.Clamp(Mathf.CeilToInt((float)(sideLength / OutlineSpacing)), 1, MaxOutlinePoints / 4);
            int count = 0;
            for (int c = 0; c < 4; c++)
            {
                int next = (c + 1) % 4;
                for (int k = 0; k < perSide && count < MaxOutlinePoints; k++)
                {
                    double t = (double)k / perSide;
                    frame.ToWorld(ci[c] + (ci[next] - ci[c]) * t, cj[c] + (cj[next] - cj[c]) * t, out double x, out double z);
                    if (!Ground((float)x, (float)z, out float y)) continue;
                    buffer[count++] = new Vector3((float)x, y + Lift, (float)z);
                }
            }
            Color color = locked ? LockedOutlineColor : OutlineColor;
            outline.startColor = color;
            outline.endColor = color;
            outline.positionCount = count;
            for (int i = 0; i < count; i++) outline.SetPosition(i, buffer[i]);
        }

        private static bool Ground(float x, float z, out float height) => Heightmap.GetHeight(new Vector3(x, 0, z), out height);

        private LineRenderer CreateLine(string name, float width, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(root.transform, false);
            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            if (material) line.sharedMaterial = material;
            return line;
        }

        private static Material CreateMaterial()
        {
            foreach (string name in new[] { "Sprites/Default", "Hidden/Internal-Colored", "UI/Default" })
            {
                Shader shader = Shader.Find(name);
                if (!shader) continue;
                return new Material(shader) { name = "Fieldsmith_Preview" };
            }
            return null;
        }
    }
}
