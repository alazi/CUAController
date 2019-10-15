using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System.Linq;
using AssetBundles;



namespace EmissiveClothing
{
    public class EmissiveClothing : MVRScript
    {
        static readonly int LIGHT_LAYER = 6;

        JSONStorableFloat intensity;

        protected List<Material[]> origMaterials = new List<Material[]>();
        protected List<Material[]> vamMaterials = new List<Material[]>();
        protected Shader vamPropShader;
        protected List<DAZSkinWrap> renderers = new List<DAZSkinWrap>();
        string Join<T>(string sep, T[] obj)
        {
            return string.Join(sep, obj.ToList().ConvertAll(x => x.ToString()).ToArray());
        }
        public override void Init()
        {
            try {

                FloatSlider(ref intensity, "Intensity", 4,
                    val => {
                        foreach (var r in renderers) {
                            r.GetComponent<Light>().intensity = val;
                        }
                    }, 0, 20, false);

                // since there is no "asset bundle loaded" callback yet, we have to wait
                StartCoroutine(WaitForAssetBundle());

            } catch (Exception e) {
                SuperController.LogError("Exception caught: " + e);
            }
        }


        private void FloatSlider(ref JSONStorableFloat output, string name, float start, JSONStorableFloat.SetFloatCallback callback, float min, float max, bool rhs)
        {
            output = new JSONStorableFloat(name, start, callback, min, max, false, true);
            RegisterFloat(output);
            CreateSlider(output, rhs);
        }



        // try to get the renderers of the loaded asset bundle for 3 seconds.
        protected IEnumerator WaitForAssetBundle()
        {
            List<DAZSkinWrap> allRenderers = new List<DAZSkinWrap>();

            float waitForSeconds = 3.0f;
            float waited = 0f;
            while (allRenderers.Count < 1 && waited <= waitForSeconds) {
                containingAtom.gameObject.GetComponentsInChildren<DAZSkinWrap>(false, allRenderers);
                allRenderers = allRenderers.Where(x => x.name == "g2 tattoo").ToList();
                waited += 0.2f;
                yield return new WaitForSeconds(0.2f);
            }
            // Assets/MeshedVR_Shaders/Subsurface/SubsurfaceTransparentComputeBuff has reduced lighting effects, but not eliminated
            AssetBundleLoadAssetOperation request = AssetBundleManager.LoadAssetAsync("z_sha", "Assets/MeshedVR_Shaders/Subsurface/SubsurfaceTransparentComputeBuff.shader", typeof(Shader));
            if (request == null) {
                SuperController.LogError("Failed to request material tab");
                yield break;
            }
            yield return StartCoroutine(request);
            vamPropShader= request.GetAsset<Shader>();
            //vamPropShader = Shader.Find("Custom/Subsurface/TransparentComputeBuff");
            SuperController.LogMessage(vamPropShader.name);

            ConvertMaterial(allRenderers);
        }


        protected void ConvertMaterial(List<DAZSkinWrap> allRenderers)
        {

            if (allRenderers.Count == 0) {
                SuperController.LogMessage("Make sure you have an .assetbundle loaded and a prop selected from the Asset dropdown. Then reload this plugin.");
                return;
            }

            // filter renderers by supported materials
            foreach (var renderer in allRenderers) {
                var origMats = renderer.GPUmaterials;
                var vamMats = new List<Material>();
                foreach (var mat in origMats) {
                        vamMats.Add(new Material(vamPropShader));
                    
                }
                if (vamMats.Count > 0) {
                    renderers.Add(renderer);
                    origMaterials.Add(origMats);
                    vamMaterials.Add(vamMats.ToArray());
                }

            }


            if (renderers.Count == 0) {
                SuperController.LogError("emissive hack target not loaded");
                return;
            }


            for (int i = 0; i < renderers.Count; i++) {
                var renderer = renderers[i];
                var materials = renderers[i].GPUmaterials;
                for (int j = 0; j < renderer.GPUmaterials.Length; j++) {
                    if (vamMaterials[i][j] == null) continue;
                    // reassign textures from unity to vam material
                    vamMaterials[i][j].name = origMaterials[i][j].name + "vamified";
                    foreach (var texture in new string[] { "_MainTex", "_SpecTex" }) {
                        try { vamMaterials[i][j].SetTexture(texture, origMaterials[i][j].GetTexture(texture)); } catch { }
                    }
                    /*
                    vamMaterials[i][j].SetTexture("_MainTex", origMaterials[i][j].GetTexture("_MainTex"));
                    vamMaterials[i][j].SetTexture("_BumpMap", origMaterials[i][j].GetTexture("_BumpMap"));
                    try { vamMaterials[i][j].SetTexture("_AlphaTex", origMaterials[i][j].GetTexture("_AlphaTex")); } catch { }
                    try { vamMaterials[i][j].SetTexture("_SpecTex", origMaterials[i][j].GetTexture("_SpecGlossMap")); } catch { }
                    try { vamMaterials[i][j].SetTexture("_GlossTex", origMaterials[i][j].GetTexture("_MetallicGlossMap")); } catch { }
                    */
                    // assign VAM material to prop renderer
                    foreach (var c in new string[] { "_Color", "_SpecColor", "_SubdermisColor" }) {
                        vamMaterials[i][j].SetColor(c, origMaterials[i][j].GetColor(c));
                    }
                    foreach (var f in new string[] {"_SpecInt", "_Shininess", "_Fresnel", "_AlphaAdjust", "_DiffOffset", "_SpecOffset", "_IBLFilter" }) {
                        vamMaterials[i][j].SetFloat(f, origMaterials[i][j].GetFloat(f));
                    }
                    
                    materials[j] = vamMaterials[i][j];
                }
                renderers[i].GPUmaterials = materials;

                renderer.BroadcastMessage("OnApplicationFocus", true);
                var light = renderer.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.intensity = 2;
                light.range = 1e6f;
                light.color = Color.white;
                light.enabled = true;
                light.cullingMask = 1<< LIGHT_LAYER;
                light.shadows = LightShadows.None;
                renderer.draw = true;
                var helper = renderer.gameObject.AddComponent<Helper>();
                //helper.mats = materials;
            }
            SuperController.LogMessage($"total {vamMaterials.SelectMany(x => x).Where(x => x != null).Count()}");


        }


        void OnDestroy()
        {
            // restore original materials
            for (int i = 0; i < renderers.Count; i++) {
                var renderer = renderers[i];
                renderers[i].GPUmaterials = origMaterials[i];
                GameObject.Destroy(renderers[i].GetComponent<Helper>());
                GameObject.Destroy(renderers[i].GetComponent<Light>());
            }
        }

        //[DefaultExecutionOrder(100)]
        class Helper : MonoBehaviour
        {
            DAZSkinWrap wrap;
            public Material[] mats;
            int i;
            void Start()
            {
                wrap = GetComponent<DAZSkinWrap>();
                mats = new Material[wrap.GPUmaterials.Length];
                Camera.onPreCull += DoOnPreCull;
            }
            void DoOnPreCull(Camera cam)
            {
                if (i % 50 == 0) {
                    //SuperController.LogMessage($"{cam.name}: {cam.cullingMask:X} ({cam.cullingMask})");
                }
                if (cam.cullingMask == 0x200001) { // reflections
                    cam.cullingMask |= 1 << LIGHT_LAYER;
                }
            }
            void OnDestroy()
            {
                Camera.onPreCull -= DoOnPreCull;
            }
            public void LateUpdate()
            {
                if (wrap.draw) {
                    //i++;
                    if (i % 40 == 0) {
                        //SuperController.LogMessage("bar");
                    }
                    //wrap.draw = false;
                    //return;
                }
                i++;
                if (i % 40 == 0) {
                    //SuperController.LogMessage("foo");
                }
                //wrap.draw = true;


                //wrap.SendMessage("LateUpdate");
                var mesh = wrap.Mesh;
                for (int i = 0; i < wrap.GPUmaterials.Length; i++) {
                    //mats[i] = new Material(wrap.GPUmaterials[i]);
                    //mats[i].SetFloat("_AlphaAdjust", 1);
                    
                    
                    // could probably  mess with things so that we change gpumaterials underneath it and try to catch every time it resets buffers
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, wrap.GPUmaterials[i], LIGHT_LAYER, null, i, null, false, false);
                    
                    
                    //wrap.GPUmaterials[i].SetFloat("_AlphaAdjust", -1);
                }

                //wrap.draw = false;
                if (i % 40 == 0) {
                    i++;
                    //SuperController.LogMessage("baz");
                }
            }

        }
        
    }
}