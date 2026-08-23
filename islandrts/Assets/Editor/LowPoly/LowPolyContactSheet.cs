using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IslandRTS.ArtGen
{
    /// <summary>
    /// Renders every generated template asset into a single contact-sheet PNG, shot from
    /// the gameplay camera angle. Faster than opening the showcase scene when you just
    /// want to check silhouettes after a parameter tweak.
    ///
    /// Output: Assets/Art/ContactSheet.png
    /// </summary>
    public static class LowPolyContactSheet
    {
        private const string OutputPath = "Assets/Art/ContactSheet.png";
        private const int CellSize = 220;
        private const int Columns = 6;

        // Same angle as the gameplay camera in MainIsland.unity.
        private static readonly Vector3 CameraEuler = new Vector3(45f, 45f, 0f);

        [MenuItem("Tools/Island RTS/Low-Poly Templates/Capture Contact Sheet", false, 61)]
        public static void Capture()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            List<AssetDef> defs = LowPolyShapes.All();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- Lighting: warm key matching the day preset, gradient ambient ----
            GameObject sunGO = new GameObject("Sun");
            Light sun = sunGO.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.79f);
            sun.intensity = 1.6f;
            sun.shadows = LightShadows.None;
            sunGO.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.60f, 0.58f, 0.50f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.42f);
            RenderSettings.ambientGroundColor = new Color(0.20f, 0.23f, 0.30f);

            GameObject camGO = new GameObject("CaptureCamera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.20f, 0.22f, 0.26f);
            camGO.transform.rotation = Quaternion.Euler(CameraEuler);

            int rows = Mathf.CeilToInt(defs.Count / (float)Columns);
            Texture2D sheet = new Texture2D(Columns * CellSize, rows * CellSize, TextureFormat.RGBA32, false);

            RenderTexture rt = new RenderTexture(CellSize, CellSize, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            rt.Create();
            cam.targetTexture = rt;

            Texture2D cell = new Texture2D(CellSize, CellSize, TextureFormat.RGBA32, false);
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                for (int i = 0; i < defs.Count; i++)
                {
                    AssetDef def = defs[i];
                    string path = "Assets/Art/Prefabs/" + def.Category + "/" + def.Name + ".prefab";
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null)
                    {
                        Debug.LogWarning("[LowPoly] Contact sheet: missing prefab " + path);
                        continue;
                    }

                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    instance.transform.position = Vector3.zero;

                    // Frame on the renderer bounds so every asset fills its cell regardless of scale.
                    Renderer r = instance.GetComponentInChildren<Renderer>();
                    Bounds bounds = r != null ? r.bounds : new Bounds(Vector3.zero, Vector3.one);
                    float extent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
                    cam.orthographicSize = Mathf.Max(extent * 1.35f, 0.15f);
                    cam.transform.position = bounds.center - cam.transform.forward * 60f;
                    cam.nearClipPlane = 0.05f;
                    cam.farClipPlane = 200f;

                    cam.Render();

                    RenderTexture.active = rt;
                    cell.ReadPixels(new Rect(0, 0, CellSize, CellSize), 0, 0);
                    cell.Apply();

                    int col = i % Columns;
                    int row = i / Columns;
                    // Texture2D origin is bottom-left; write rows top-down so the sheet
                    // reads in the same order as the log listing.
                    int destY = (rows - 1 - row) * CellSize;
                    sheet.SetPixels(col * CellSize, destY, CellSize, CellSize, cell.GetPixels());

                    Object.DestroyImmediate(instance);
                }

                sheet.Apply();
                File.WriteAllBytes(OutputPath, sheet.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prevActive;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(cell);
                Object.DestroyImmediate(sheet);
            }

            AssetDatabase.Refresh();

            System.Text.StringBuilder order = new System.Text.StringBuilder();
            order.AppendLine("[LowPoly] Contact sheet written to " + OutputPath + " (" + Columns + " columns, left to right, top to bottom):");
            for (int i = 0; i < defs.Count; i++)
            {
                order.Append(defs[i].Name);
                order.Append((i % Columns == Columns - 1 || i == defs.Count - 1) ? "\n" : ", ");
            }
            Debug.Log(order.ToString());
        }
    }
}
