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

        //UnityEngine.AssetBundle bundle;

        // try to get the renderers of the loaded asset bundle for 3 seconds.
        protected IEnumerator WaitForAssetBundle()
        {
            List<DAZSkinWrap> allRenderers = new List<DAZSkinWrap>();
            var tmp = Shader.Find("Custom/Alazi/ExtraEmissiveComputeBuff");
            if (tmp != null) {
                SuperController.LogMessage("already there");
                //Resources.UnloadAsset(tmp);
            }

            // So, AssetBundle.LoadFromFileAsync refuses to compile for some reason, 
            // and trying to unload using MeshVR.AssetLoader (after OnDestroy + coroutine)
            // crashes unity for some reason anyways,so we'll just do the simple thing of never unloading

            AssetBundleLoadAssetOperation request = AssetBundleManager.LoadAssetAsync(@"..\..\Custom\Assets\emissiveshader.assetbundle",
                "Assets/EmissiveHack.shader", typeof(Shader));
            
            if (request == null) {
                SuperController.LogError("Failed to request shader");
                yield break;
            }
            yield return StartCoroutine(request);
            vamPropShader = request.GetAsset<Shader>();
            if (vamPropShader == null) {
                SuperController.LogError("Failed to load asset");
                yield break;
            }

            

            SuperController.LogMessage(vamPropShader.name);
            SuperController.LogMessage($"find: {Shader.Find("Custom/Alazi/ExtraEmissiveComputeBuff")}");


            float waitForSeconds = 3.0f;
            float waited = 0f;
            while (allRenderers.Count < 1 && waited <= waitForSeconds) {
                containingAtom.gameObject.GetComponentsInChildren<DAZSkinWrap>(false, allRenderers);
                allRenderers = allRenderers.Where(x => x.name == "g2 tattoo").ToList();
                waited += 0.2f;
                yield return new WaitForSeconds(0.2f);
            }
            SuperController.LogMessage($"{allRenderers.Count}");
            foreach (var item in allRenderers[0].GetComponents<Component>()) {
                //SuperController.LogMessage(item.ToString());
            }
            
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

                if (renderer.ToString().Contains("Emissive")) { SuperController.LogError($"found dup {renderer}"); continue; }

                for (int j = 0; j < renderer.GPUmaterials.Length; j++) {
                    if (vamMaterials[i][j] == null) continue;
                    // reassign textures from unity to vam material
                    vamMaterials[i][j].name = origMaterials[i][j].name;
                    foreach (var texture in new string[] { "_MainTex", }) {
                        try { vamMaterials[i][j].SetTexture(texture, origMaterials[i][j].GetTexture(texture)); } catch { }
                    }

                    // assign VAM material to prop renderer
                    foreach (var c in new string[] { "_Color", }) {
                        vamMaterials[i][j].SetColor(c, origMaterials[i][j].GetColor(c));
                    }
                    foreach (var f in new string[] { "_AlphaAdjust" }) {
                        vamMaterials[i][j].SetFloat(f, origMaterials[i][j].GetFloat(f));
                    }

                }
                //renderers[i].GPUmaterials = materials;

                renderer.BroadcastMessage("OnApplicationFocus", true);

                var light = renderer.gameObject.AddComponent<Light>();
                light.type = LightType.Point;
                light.intensity = 2;
                light.range = 1e6f;
                light.color = Color.white;
                light.enabled = false;
                light.cullingMask = 1<< LIGHT_LAYER;
                light.shadows = LightShadows.None;
                renderer.draw = true;
                var helper = renderer.gameObject.AddComponent<Helper>();
                renderer.gameObject.AddComponent<EmissiveSkinWrap>().CopyFrom(renderer, null);
                helper.mats = vamMaterials[i];
            }
            SuperController.LogMessage($"total {vamMaterials.SelectMany(x => x).Where(x => x != null).Count()}");


        }


        // if hdr emissiveColor.rgb = exp2(-emissiveColor.rgb); ??
        //tex2D(_EmissionMap, uv).rgb * _EmissionColor.rgb;
        void OnDestroy()
        {
            // restore original materials
            for (int i = 0; i < renderers.Count; i++) {
                var renderer = renderers[i];

                GameObject.Destroy(renderers[i].gameObject?.GetComponent<Helper>());
                GameObject.Destroy(renderers[i].gameObject?.GetComponent<Light>());
                GameObject.Destroy(renderers[i].gameObject?.GetComponent<EmissiveSkinWrap>());
                renderer.draw = true;
                var control = GetComponent<DAZSkinWrapControl>();
                if (control && (control.wrap == null || control.wrap == this)) {
                    control.wrap = renderer;
                }
            }
        }

        class EmissiveSkinWrap : DAZSkinWrap
        {


            public Dictionary<int, ComputeBuffer> MaterialVertsBuffers { get { return materialVertsBuffers; } set { materialVertsBuffers = value; } }
            public Dictionary<int, ComputeBuffer> MaterialNormalsBuffers { get { return materialNormalsBuffers; } set { materialNormalsBuffers = value; } }
            public Dictionary<int, ComputeBuffer> MaterialTangentsBuffers { get { return materialTangentsBuffers; } set { materialTangentsBuffers = value; } }

            public void CopyFrom(DAZSkinWrap wrap, Material[] newMats)
            {
                base.skinTransform = wrap.skinTransform;
                base.skin = wrap.skin;
                base.dazMesh = wrap.dazMesh;
                base.GPUSkinWrapper = wrap.GPUSkinWrapper;
                base.GPUMeshCompute = wrap.GPUMeshCompute;
                base.CopyMaterials();
                base.GPUmaterials = wrap.GPUmaterials.Select(m => new Material(m)).ToArray();
                //base.GPUmaterials = newMats;

                base.wrapName = wrap.wrapName;
                base.wrapStore = wrap.wrapStore;

                wrap.draw = false;
                base.draw = true;

                base.surfaceOffset = wrap.surfaceOffset;
                base.defaultSurfaceOffset = wrap.defaultSurfaceOffset;
                base.additionalThicknessMultiplier = wrap.additionalThicknessMultiplier;
                base.defaultAdditionalThicknessMultiplier = wrap.defaultAdditionalThicknessMultiplier;

                var control = GetComponent<DAZSkinWrapControl>();
                if (control && (control.wrap == null || control.wrap == wrap)) {

                    
                    control.wrap = this;
                }

            }


        }

        //[DefaultExecutionOrder(100)]
        class Helper : MonoBehaviour
        {
            EmissiveSkinWrap wrap;
            public Material[] mats;
            int i;

            protected Dictionary<int, ComputeBuffer> materialVertsBuffers = new Dictionary<int, ComputeBuffer>();

            protected Dictionary<int, ComputeBuffer> materialNormalsBuffers = new Dictionary<int, ComputeBuffer>();

            protected Dictionary<int, ComputeBuffer> materialTangentsBuffers = new Dictionary<int, ComputeBuffer>();

            void Start()
            {
                wrap = GetComponent<EmissiveSkinWrap>();
            }

            public void LateUpdate()
            {
                if (mats == null) {
                    SuperController.LogError("broken emissive helper");
                    this.enabled = false;
                    return;
                }
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
                    //SuperController.LogMessage($"{wrap.MaterialVertsBuffers.Count}");
                }
                //wrap.draw = true;


                //wrap.SendMessage("LateUpdate");
                var mesh = wrap.Mesh;
                for (int i = 0; i < wrap.GPUmaterials.Length; i++) {
                    //mats[i] = new Material(wrap.GPUmaterials[i]);
                    //mats[i].SetFloat("_AlphaAdjust", 1);
                    UpdateBuffer(materialVertsBuffers, wrap.MaterialVertsBuffers, "verts", i);
                    UpdateBuffer(materialNormalsBuffers, wrap.MaterialNormalsBuffers, "normals", i);
                    UpdateBuffer(materialTangentsBuffers, wrap.MaterialTangentsBuffers, "tangents", i);

                    Graphics.DrawMesh(mesh, Matrix4x4.identity, mats[i], 0, null, i, null, false, false);
                    
                    
                    //wrap.GPUmaterials[i].SetFloat("_AlphaAdjust", -1);
                }

                //wrap.draw = false;
                if (i % 40 == 0) {
                    i++;
                    //SuperController.LogMessage("baz");
                }
            }

            private void UpdateBuffer(Dictionary<int, ComputeBuffer> ours, Dictionary<int, ComputeBuffer> theirs, string buf, int i)
            {
                ComputeBuffer ourB, theirB;
                ours.TryGetValue(i, out ourB);
                theirs.TryGetValue(i, out theirB);
                if (ourB != theirB) {
                    mats[i].SetBuffer(buf, theirB);
                    ours[i] = theirB;
                }
            }
        }
        
    }
}