using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Flat-shaded mesh builder.
    ///
    /// Every triangle gets its own three vertices and a computed face normal, so hard
    /// edges are guaranteed without any smoothing-angle import settings. That faceted
    /// read IS the low-poly look - see the "per-triangle normals" note in CLAUDE.md's
    /// Visual / Art Gotchas section.
    ///
    /// Geometry is emitted into named groups; each group becomes one submesh and gets
    /// one material, so a palm is trunk-submesh + frond-submesh rather than two objects.
    ///
    /// All randomness runs through a seeded System.Random rather than UnityEngine.Random,
    /// so re-running the generator reproduces identical meshes.
    /// </summary>
    public class MeshBuilder
    {
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> normals = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();

        private readonly List<string> groupOrder = new List<string>();
        private readonly Dictionary<string, List<int>> groups = new Dictionary<string, List<int>>();

        private string currentGroup;
        private Color currentColor = Color.white;

        private Matrix4x4 matrix = Matrix4x4.identity;
        private readonly Stack<Matrix4x4> matrixStack = new Stack<Matrix4x4>();

        private System.Random rng;

        public MeshBuilder(int seed = 0)
        {
            rng = new System.Random(seed);
            Use("RockMid");
        }

        // ------------------------------------------------------------------
        // Material groups
        // ------------------------------------------------------------------

        /// <summary>Subsequent geometry goes into the submesh for this palette key.</summary>
        public MeshBuilder Use(string paletteKey)
        {
            currentGroup = paletteKey;
            currentColor = LowPolyPalette.Get(paletteKey).Color;
            if (!groups.ContainsKey(paletteKey))
            {
                groups[paletteKey] = new List<int>();
                groupOrder.Add(paletteKey);
            }
            return this;
        }

        // ------------------------------------------------------------------
        // Transform stack
        // ------------------------------------------------------------------

        public MeshBuilder Push() { matrixStack.Push(matrix); return this; }
        public MeshBuilder Pop() { matrix = matrixStack.Pop(); return this; }

        public MeshBuilder Translate(Vector3 t)
        {
            matrix *= Matrix4x4.Translate(t);
            return this;
        }

        public MeshBuilder Translate(float x, float y, float z) { return Translate(new Vector3(x, y, z)); }

        public MeshBuilder Rotate(float xDeg, float yDeg, float zDeg)
        {
            matrix *= Matrix4x4.Rotate(Quaternion.Euler(xDeg, yDeg, zDeg));
            return this;
        }

        public MeshBuilder Rotate(Quaternion q)
        {
            matrix *= Matrix4x4.Rotate(q);
            return this;
        }

        public MeshBuilder RotateY(float deg) { return Rotate(0f, deg, 0f); }

        public MeshBuilder Scale(Vector3 s)
        {
            matrix *= Matrix4x4.Scale(s);
            return this;
        }

        public MeshBuilder Scale(float uniform) { return Scale(Vector3.one * uniform); }

        // ------------------------------------------------------------------
        // Seeded randomness (deterministic across runs)
        // ------------------------------------------------------------------

        public void Reseed(int seed) { rng = new System.Random(seed); }

        public float Rand(float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        public int RandInt(int minInclusive, int maxExclusive)
        {
            return rng.Next(minInclusive, maxExclusive);
        }

        // ------------------------------------------------------------------
        // Core primitives
        // ------------------------------------------------------------------

        /// <summary>
        /// When true, every triangle is also emitted reversed. Used for flat foliage cards
        /// (palm fronds, grass, leaves) so they read correctly from underneath, which
        /// matters on a rotating RTS camera.
        /// </summary>
        public bool DoubleSided { get; set; }

        /// <summary>Adds one triangle. Winding a-b-c faces the viewer in Unity's left-handed space.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            AddTriangle(a, b, c);
            if (DoubleSided) AddTriangle(a, c, b);
        }

        private void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 wa = matrix.MultiplyPoint3x4(a);
            Vector3 wb = matrix.MultiplyPoint3x4(b);
            Vector3 wc = matrix.MultiplyPoint3x4(c);

            Vector3 n = Vector3.Cross(wb - wa, wc - wa);
            if (n.sqrMagnitude < 1e-12f) return; // degenerate - skip rather than emit NaN normals
            n.Normalize();

            int baseIndex = verts.Count;
            AddVertex(wa, n);
            AddVertex(wb, n);
            AddVertex(wc, n);

            List<int> tris = groups[currentGroup];
            tris.Add(baseIndex);
            tris.Add(baseIndex + 1);
            tris.Add(baseIndex + 2);
        }

        /// <summary>Adds a quad as two triangles. Vertices must be given in order around the perimeter.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(a, b, c);
            Tri(a, c, d);
        }

        /// <summary>Fills an n-gon as a triangle fan. Points must be ordered around the perimeter.</summary>
        public void Ngon(IList<Vector3> points, bool reverse = false)
        {
            int n = points.Count;
            if (n < 3) return;
            for (int i = 1; i < n - 1; i++)
            {
                if (reverse) Tri(points[0], points[i + 1], points[i]);
                else Tri(points[0], points[i], points[i + 1]);
            }
        }

        private void AddVertex(Vector3 p, Vector3 n)
        {
            verts.Add(p);
            normals.Add(n);
            colors.Add(currentColor);

            // Cheap planar UVs projected on the dominant normal axis. The template
            // materials are flat colours so this only matters if a texture is added
            // later, but it keeps the meshes from being unusable in that case.
            float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
            if (ay >= ax && ay >= az) uvs.Add(new Vector2(p.x, p.z));
            else if (ax >= az) uvs.Add(new Vector2(p.z, p.y));
            else uvs.Add(new Vector2(p.x, p.y));
        }

        // ------------------------------------------------------------------
        // Shape helpers
        // ------------------------------------------------------------------

        /// <summary>Axis-aligned box centred on <paramref name="center"/>.</summary>
        public void Box(Vector3 center, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = center + new Vector3(-h.x, -h.y, -h.z);
            Vector3 p100 = center + new Vector3(h.x, -h.y, -h.z);
            Vector3 p110 = center + new Vector3(h.x, h.y, -h.z);
            Vector3 p010 = center + new Vector3(-h.x, h.y, -h.z);
            Vector3 p001 = center + new Vector3(-h.x, -h.y, h.z);
            Vector3 p101 = center + new Vector3(h.x, -h.y, h.z);
            Vector3 p111 = center + new Vector3(h.x, h.y, h.z);
            Vector3 p011 = center + new Vector3(-h.x, h.y, h.z);

            Quad(p000, p010, p110, p100); // -Z
            Quad(p101, p111, p011, p001); // +Z
            Quad(p001, p011, p010, p000); // -X
            Quad(p100, p110, p111, p101); // +X
            Quad(p010, p011, p111, p110); // +Y
            Quad(p001, p000, p100, p101); // -Y
        }

        /// <summary>Box whose pivot sits on its base rather than its centre.</summary>
        public void BoxOnGround(Vector3 baseCenter, Vector3 size)
        {
            Box(baseCenter + new Vector3(0f, size.y * 0.5f, 0f), size);
        }

        /// <summary>Rectangular frustum: a box with independently sized top and bottom faces.</summary>
        public void Frustum(Vector3 baseCenter, Vector2 baseSize, Vector2 topSize, float height,
                            float topYawDeg = 0f, Vector2 topOffset = default(Vector2))
        {
            Vector3[] bottom = new Vector3[4];
            Vector3[] top = new Vector3[4];

            Vector2 bh = baseSize * 0.5f;
            Vector2 th = topSize * 0.5f;
            Quaternion yaw = Quaternion.Euler(0f, topYawDeg, 0f);
            Vector3 topCenter = baseCenter + new Vector3(topOffset.x, height, topOffset.y);

            bottom[0] = baseCenter + new Vector3(-bh.x, 0f, -bh.y);
            bottom[1] = baseCenter + new Vector3(bh.x, 0f, -bh.y);
            bottom[2] = baseCenter + new Vector3(bh.x, 0f, bh.y);
            bottom[3] = baseCenter + new Vector3(-bh.x, 0f, bh.y);

            top[0] = topCenter + yaw * new Vector3(-th.x, 0f, -th.y);
            top[1] = topCenter + yaw * new Vector3(th.x, 0f, -th.y);
            top[2] = topCenter + yaw * new Vector3(th.x, 0f, th.y);
            top[3] = topCenter + yaw * new Vector3(-th.x, 0f, th.y);

            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(bottom[i], top[i], top[j], bottom[j]);
            }

            // Perimeter points run counter-clockwise seen from above, and Unity treats
            // clockwise-from-the-front as front-facing, so the TOP cap is the reversed
            // fan and the bottom cap is the forward one.
            Ngon(top, true);
            Ngon(bottom);
        }

        /// <summary>
        /// N-sided prism / cone / cylinder. Set <paramref name="rTop"/> to 0 for a cone,
        /// or equal to rBottom for a straight cylinder.
        /// </summary>
        public void Prism(Vector3 baseCenter, float rBottom, float rTop, float height, int sides,
                          float twistDeg = 0f, bool capBottom = true, bool capTop = true, float startAngleDeg = 0f)
        {
            sides = Mathf.Max(3, sides);
            Vector3[] bottom = new Vector3[sides];
            Vector3[] top = new Vector3[sides];
            Vector3 topCenter = baseCenter + new Vector3(0f, height, 0f);

            for (int i = 0; i < sides; i++)
            {
                float t = (float)i / sides;
                float aB = (startAngleDeg + t * 360f) * Mathf.Deg2Rad;
                float aT = (startAngleDeg + twistDeg + t * 360f) * Mathf.Deg2Rad;
                bottom[i] = baseCenter + new Vector3(Mathf.Cos(aB) * rBottom, 0f, Mathf.Sin(aB) * rBottom);
                top[i] = topCenter + new Vector3(Mathf.Cos(aT) * rTop, 0f, Mathf.Sin(aT) * rTop);
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                if (rTop <= 0.0001f) Tri(bottom[i], topCenter, bottom[j]);
                else if (rBottom <= 0.0001f) Tri(baseCenter, top[i], top[j]);
                else Quad(bottom[i], top[i], top[j], bottom[j]);
            }

            // See the note in Frustum: top cap is the reversed fan, bottom cap the forward one.
            if (capTop && rTop > 0.0001f) Ngon(top, true);
            if (capBottom && rBottom > 0.0001f) Ngon(bottom);
        }

        /// <summary>Pyramid with a square base sitting on the ground plane.</summary>
        public void Pyramid(Vector3 baseCenter, float baseSize, float height)
        {
            float h = baseSize * 0.5f;
            Vector3 apex = baseCenter + new Vector3(0f, height, 0f);
            Vector3 p0 = baseCenter + new Vector3(-h, 0f, -h);
            Vector3 p1 = baseCenter + new Vector3(h, 0f, -h);
            Vector3 p2 = baseCenter + new Vector3(h, 0f, h);
            Vector3 p3 = baseCenter + new Vector3(-h, 0f, h);

            Tri(p0, apex, p1);
            Tri(p1, apex, p2);
            Tri(p2, apex, p3);
            Tri(p3, apex, p0);
            Quad(p0, p1, p2, p3); // underside, so the shape reads solid from a low camera
        }

        /// <summary>Gable (two-slope) roof with an optional eave overhang.</summary>
        public void GableRoof(Vector3 baseCenter, float width, float depth, float height, float overhang = 0f)
        {
            float w = width * 0.5f + overhang;
            float d = depth * 0.5f + overhang;

            Vector3 f0 = baseCenter + new Vector3(-w, 0f, -d);
            Vector3 f1 = baseCenter + new Vector3(w, 0f, -d);
            Vector3 f2 = baseCenter + new Vector3(w, 0f, d);
            Vector3 f3 = baseCenter + new Vector3(-w, 0f, d);

            Vector3 ridgeA = baseCenter + new Vector3(0f, height, -d);
            Vector3 ridgeB = baseCenter + new Vector3(0f, height, d);

            Quad(f0, f3, ridgeB, ridgeA); // -X slope
            Quad(f1, ridgeA, ridgeB, f2); // +X slope
            Tri(f0, ridgeA, f1);          // -Z gable end
            Tri(f2, ridgeB, f3);          // +Z gable end
            Quad(f0, f1, f2, f3);         // underside
        }

        /// <summary>
        /// Irregular boulder: a low-density lat/long sphere with every vertex jittered,
        /// then flat-shaded. Deterministic for a given seed.
        /// </summary>
        public void Rock(Vector3 baseCenter, Vector3 size, float jitter = 0.18f, int rings = 3, int segments = 7)
        {
            rings = Mathf.Max(2, rings);
            segments = Mathf.Max(4, segments);

            Vector3 half = size * 0.5f;
            Vector3 center = baseCenter + new Vector3(0f, half.y, 0f);

            Vector3[,] grid = new Vector3[rings + 1, segments];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * (r + 0.5f) / (rings + 1); // skip exact poles
                for (int s = 0; s < segments; s++)
                {
                    float theta = 2f * Mathf.PI * s / segments;
                    Vector3 unit = new Vector3(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));

                    float j = 1f + Rand(-jitter, jitter);
                    Vector3 p = center + Vector3.Scale(unit * j, half);
                    p.y = Mathf.Max(p.y, baseCenter.y); // keep the boulder from dipping below ground
                    grid[r, s] = p;
                }
            }

            Vector3 topPole = center + new Vector3(
                Rand(-jitter, jitter) * half.x,
                half.y * (1f + Rand(-jitter, jitter)),
                Rand(-jitter, jitter) * half.z);
            Vector3 bottomPole = new Vector3(
                center.x + Rand(-jitter, jitter) * half.x,
                baseCenter.y,
                center.z + Rand(-jitter, jitter) * half.z);

            // Rings run counter-clockwise seen from above and r increases downward, so
            // the outward-facing traversal is across-then-down, not down-then-across.
            for (int s = 0; s < segments; s++)
            {
                int s2 = (s + 1) % segments;
                Tri(topPole, grid[0, s2], grid[0, s]);
                Tri(bottomPole, grid[rings, s], grid[rings, s2]);
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int s2 = (s + 1) % segments;
                    Quad(grid[r, s], grid[r, s2], grid[r + 1, s2], grid[r + 1, s]);
                }
            }
        }

        /// <summary>Box-section beam running from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void Beam(Vector3 a, Vector3 b, float width, float thickness)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-5f) return;
            Vector3 unit = dir / len;

            Push();
            matrix *= Matrix4x4.TRS(
                (a + b) * 0.5f,
                Quaternion.LookRotation(unit, Mathf.Abs(unit.y) > 0.99f ? Vector3.forward : Vector3.up),
                Vector3.one);
            Box(Vector3.zero, new Vector3(width, thickness, len));
            Pop();
        }

        /// <summary>Round log running from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public void LogBetween(Vector3 a, Vector3 b, float radius, int sides = 6)
        {
            TaperedSegment(a, b, radius, radius, sides);
        }

        /// <summary>
        /// Tapered round segment from <paramref name="a"/> to <paramref name="b"/>. Chain
        /// these to build curved, thinning forms like palm trunks.
        /// </summary>
        public void TaperedSegment(Vector3 a, Vector3 b, float rA, float rB, int sides = 6, float twistDeg = 0f)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            if (len < 1e-5f) return;

            Push();
            matrix *= Matrix4x4.TRS(a, Quaternion.FromToRotation(Vector3.up, dir / len), Vector3.one);
            // Caps are skipped: chained segments hide them, and the ends are covered by
            // whatever sits on top (foliage, a rock, the ground).
            Prism(Vector3.zero, rA, rB, len, sides, twistDeg, false, false);
            Pop();
        }

        // ------------------------------------------------------------------
        // Output
        // ------------------------------------------------------------------

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (var kv in groups) n += kv.Value.Count / 3;
                return n;
            }
        }

        /// <summary>Palette keys in submesh order - index i here is material slot i on the renderer.</summary>
        public List<string> MaterialKeys
        {
            get
            {
                List<string> used = new List<string>();
                for (int i = 0; i < groupOrder.Count; i++)
                    if (groups[groupOrder[i]].Count > 0) used.Add(groupOrder[i]);
                return used;
            }
        }

        public Mesh ToMesh(string name)
        {
            List<string> used = MaterialKeys;

            Mesh mesh = new Mesh();
            mesh.name = name;
            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);

            mesh.subMeshCount = used.Count;
            for (int i = 0; i < used.Count; i++)
                mesh.SetTriangles(groups[used[i]], i, true);

            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
