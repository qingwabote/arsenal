using UnityEngine;

namespace Arsenal
{
    /// <summary>
    /// The static ribbon topology: K segments x 2 sides, (K+1) x 2 = 50 vertices, K quads.
    /// Vertex payload lives in UV: x = u in [0,1] (tail to head), y = side in {0,1}
    /// (the shader expands it to -1/+1 across the tangent). Positions form a flat XZ ladder
    /// (u along -z, side along x) — the vertex shader replaces them with history-texture
    /// world positions, but any raw-geometry fallback (editor preview, unlit debug) stays
    /// visible to a horizontal camera instead of vanishing edge-on. Bounds cover the ribbon
    /// reach so culling follows the head without clipping the tail.
    /// </summary>
    public static class RibbonMeshFactory
    {
        public static Mesh Create(int k)
        {
            var mesh = new Mesh { name = $"TrailRibbon-{k}" };

            var verts = new Vector3[(k + 1) * 2];
            var uvs = new Vector2[(k + 1) * 2];
            for (int r = 0; r <= k; r++)
            {
                float u = (float)r / k;
                // 惯例:u=1 行位于 +Z(=头),side 沿 +X。uv.x 用"距头距离"(头行=0=纹理左边),
                // 使几何朝向/贴图设计/飞行方向三者按常识对齐;shader 插值参数取 1-uv.x。
                verts[r * 2 + 0] = new Vector3(0f, 0f, u);
                verts[r * 2 + 1] = new Vector3(1f, 0f, u);
                uvs[r * 2 + 0] = new Vector2(1f - u, 0f);
                uvs[r * 2 + 1] = new Vector2(1f - u, 1f);
            }

            var indices = new int[k * 6];
            for (int r = 0; r < k; r++)
            {
                int a = r * 2, b = a + 1, c = a + 2, d = a + 3;
                indices[r * 6 + 0] = a; indices[r * 6 + 1] = c; indices[r * 6 + 2] = b;
                indices[r * 6 + 3] = b; indices[r * 6 + 4] = c; indices[r * 6 + 5] = d;
            }

            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.SetIndices(indices, MeshTopology.Triangles, 0);
            float reach = 5f;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(reach, reach, reach));
            return mesh;
        }
    }
}
