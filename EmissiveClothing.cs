﻿using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AssetBundles;

namespace EmissiveClothing
{
    public class EmissiveClothing : MVRScript
    {
        JSONStorableFloat alpha;
        JSONStorableBool renderOriginal;
        JSONStorableUrl loadedShaderPath = new JSONStorableUrl("shader", "");

        private static readonly string BUNDLE_NAME = "emissiveshader.assetbundle";

        protected List<Material[]> ourMaterials = new List<Material[]>();
        protected Shader shader;
        protected List<DAZSkinWrap> wraps = new List<DAZSkinWrap>();
        protected JSONStorableColor color;

        public override void Init()
        {
            try {
                color = new JSONStorableColor("Color", HSVColorPicker.RGBToHSV(1f, 1f, 1f), _ => SyncMats());
                RegisterColor(color);
                CreateColorPicker(color, true);
                FloatSlider(ref alpha, "Color Alpha", 1,
                    _ => { SyncMats(); }, 0, 1, true);

                renderOriginal = new JSONStorableBool("Render Original Material", true);
                RegisterBool(renderOriginal);
                CreateToggle(renderOriginal, false);

                RegisterUrl(loadedShaderPath);

                CreateButton("Rescan active clothes").button.onClick.AddListener(() => {
                    StartCoroutine(Rebuild());
                    });
                
                StartCoroutine(LoadShaderAndInit());
            } catch (Exception e) {
                SuperController.LogError("Exception caught: " + e);
            }
        }

        string GetPluginPath() // basically straight from VAMDeluxe's Dollmaster
        {
            string pluginId = this.storeId.Split('_')[0];
            string pathToScriptFile = manager.GetJSON(true, true)["plugins"][pluginId].Value;
            string pathToScriptFolder = pathToScriptFile.Substring(0, pathToScriptFile.LastIndexOfAny(new char[] { '/', '\\' }));
            return pathToScriptFolder;
        }

        private void SyncMats()
        {
            foreach (var r in ourMaterials) {
                foreach (var m in r) {
                    if (m == null)
                        continue;
                    Color c = color.colorPicker.currentColor;
                    c.a = alpha.val;
                    m.SetColor("_Color", c);
                }
            }
        }

        private void FloatSlider(ref JSONStorableFloat output, string name, float start, JSONStorableFloat.SetFloatCallback callback, float min, float max, bool rhs)
        {
            output = new JSONStorableFloat(name, start, callback, min, max, false, true);
            RegisterFloat(output);
            CreateSlider(output, rhs);
        }

        // Everything about all this shader loading is a horrible hack
        private IEnumerator AttemptLoadShader(string url)
        {
            if (shader != null)
                yield break;
            // So, AssetBundle.LoadFromFileAsync refuses to compile for some reason, 
            // and trying to unload using MeshVR.AssetLoader (after OnDestroy + coroutine delay several frames)
            // crashes unity for some reason anyways,so we'll just do the simple thing of never unloading

            // LoadAssetAsync is relative to StreamingAssets
            AssetBundleLoadAssetOperation request = AssetBundleManager.LoadAssetAsync("..\\..\\" + url, "Assets/EmissiveHack.shader", typeof(Shader));

            if (request == null) {
                yield break;
            }
            yield return StartCoroutine(request);
            shader = request.GetAsset<Shader>();
            if (shader != null)
                loadedShaderPath.val = url;
        }

        // If there are multiple copies of this script loaded (over the course of a session, even), 
        // we don't want to fail because unity refuses to reload an identical assetbundle from a different path
        private static readonly string STASH_NAME = "EmissiveShaderStash";
        private void StashShader()
        {
            var go = new GameObject(STASH_NAME);
            go.SetActive(false);
            var rend = go.AddComponent<LineRenderer>();
            rend.material = new Material(shader);
            rend.material.name = loadedShaderPath.val;
            rend.enabled = false;
            go.transform.parent = Singleton<Localizatron>.Instance.gameObject.transform;
        }
        private Shader FindStashedShader()
        {
            var trans = Singleton<Localizatron>.Instance.gameObject.transform.Find(STASH_NAME);
            if (trans == null)
                return null;
            var mat = trans.gameObject.GetComponent<LineRenderer>().material;
            if (loadedShaderPath.val == "")
                loadedShaderPath.val = mat.name;
            return mat.shader;
        }

        protected IEnumerator LoadShaderAndInit()
        {
            // Wait until json load is done
            yield return new WaitForEndOfFrame();
            shader = FindStashedShader();

            if (shader == null) {
                if (loadedShaderPath.val != "")
                    yield return AttemptLoadShader(loadedShaderPath.val);
                yield return AttemptLoadShader($"{GetPluginPath()}/{BUNDLE_NAME}");
                yield return AttemptLoadShader($"Custom\\Assets\\{BUNDLE_NAME}");

                if (shader == null) {
                    SuperController.LogError("Failed to load shader");
                    yield break;
                }
                StashShader();
            }

            Build();
        }


        protected IEnumerator Rebuild()
        {
            OnDestroy();
            // wait for components to be destroyed
            yield return new WaitForEndOfFrame();
            Build();
        }
        protected void Build()
        {
            var allWraps = containingAtom.gameObject.GetComponentsInChildren<DAZSkinWrap>(false);
            ourMaterials = new List<Material[]>();
            wraps = new List<DAZSkinWrap>();
            if (allWraps.Length == 0) {
                SuperController.LogMessage("No clothes loaded");
                return;
            }

            foreach (var wrap in allWraps) {
                if (wrap.ToString().Contains("Emissive")) {
                    SuperController.LogError($"EmissiveClothing: found dup {wrap}");
                    continue;
                }
                if (wrap.skin.delayDisplayOneFrame) {
                    SuperController.LogError($"EmissiveClothing: {wrap} is delayed, not set up to handle that");
                    continue;
                }
                var ourMats = new Material[wrap.GPUmaterials.Length];
                var theirNewMats = wrap.GPUmaterials.ToArray();
                bool foundAny = false;
                
                foreach (var mo in wrap.GetComponents<DAZSkinWrapMaterialOptions>()) {
                    if (!mo.overrideId.Contains("(em)"))
                        continue;
                    // too lazy to duplicate all the code for slots2 / simpleMaterial
                    if (mo.paramMaterialSlots?.Length == 0) 
                        continue;
                    foundAny = true;
                    
                    foreach (var i in mo.paramMaterialSlots) {
                        var mat = wrap.GPUmaterials[i];
                        var ourMat = new Material(shader);
                        ourMats[i] = ourMat;
                        ourMat.name = mat.name;

                        // Ideally we'd hook all the config stuff in MaterialOptions, but that would 
                        // require too much effort to reimplement all the url/tile/offset->texture code 
                        // or to copy the existing one to override the relevant methods
                        // So require the user to hit rescan manually.
                        ourMat.SetTexture("_MainTex", mat.GetTexture("_DecalTex"));
                        mat.SetTexture("_DecalTex", null);

                        // could maybe get some tiny extra performance by using a null shader instead
                        theirNewMats[i] = new Material(mat);
                        theirNewMats[i].SetFloat("_AlphaAdjust", -1);
                    }
                }
                if (!foundAny)
                    continue;

                ourMaterials.Add(ourMats);

                wrap.BroadcastMessage("OnApplicationFocus", true);
                wrap.gameObject.AddComponent<EmissiveDAZSkinWrap>().CopyFrom(wrap, theirNewMats, ourMats, renderOriginal);
                wraps.Add(wrap);
            }

            SyncMats();
        }


        void OnDestroy()
        {
            for (int i = 0; i < wraps.Count; i++) {
                var wrap = wraps[i];
                var mats = ourMaterials[i];
                for (int j = 0; j < mats.Length; j++) {
                    if (mats[j] == null)
                        continue;
                    // If it's been changed, don't reset it
                    if (wrap.GPUmaterials[j].GetTexture("_DecalTex") == null)
                        wrap.GPUmaterials[j].SetTexture("_DecalTex", mats[j].GetTexture("_MainTex"));
                }

                GameObject.Destroy(wrap.gameObject?.GetComponent<EmissiveDAZSkinWrap>());
                wrap.draw = true;
                var control = wrap.gameObject?.GetComponent<DAZSkinWrapControl>();
                if (control && (control.wrap == null || control.wrap == this)) {
                    control.wrap = wrap;
                }
            }
        }

        // The only thing we absolutely need and can't get normally from 
        // DAZSkinWrap is the ComputeBuffers; Material.GetBuffer does not exist
        class EmissiveDAZSkinWrap : DAZSkinWrap
        {
            Dictionary<int, ComputeBuffer> emissiveMaterialVertsBuffers = new Dictionary<int, ComputeBuffer>();

            JSONStorableBool renderOriginal;
            bool oldOriginal;
            Material[] hiddenMats;
            Material[] emissiveMats;

            public void CopyFrom(DAZSkinWrap wrap, Material[] hiddenMats, Material[] emissiveMats, JSONStorableBool renderOriginal)
            {
                base.skinTransform = wrap.skinTransform;
                base.skin = wrap.skin;
                base.dazMesh = wrap.dazMesh;
                base.GPUSkinWrapper = wrap.GPUSkinWrapper;
                base.GPUMeshCompute = wrap.GPUMeshCompute;
                base.CopyMaterials();
                base.GPUmaterials = wrap.GPUmaterials;
                this.renderOriginal = renderOriginal;
                this.hiddenMats = hiddenMats;
                this.emissiveMats = emissiveMats;

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


            void LateUpdate() // Overwrite base one to possibly not render some mats
            {
                UpdateVertsGPU();
                if (renderOriginal.val != oldOriginal) {
                    materialVertsBuffers.Clear();
                    materialNormalsBuffers.Clear();
                    materialTangentsBuffers.Clear();
                    oldOriginal = renderOriginal.val;
                }
                if (!renderOriginal.val) {
                    var temp = base.GPUmaterials;
                    base.GPUmaterials = hiddenMats;
                    DrawMeshGPU();
                    base.GPUmaterials = temp;
                } else {
                    DrawMeshGPU();
                }
                
                for (int i = 0; i < emissiveMats.Length; i++) {
                    if (emissiveMats[i] == null)
                        continue;
                    emissiveMats[i].renderQueue = base.GPUmaterials[i].renderQueue; // could probably get away without updating this past initialization
                    UpdateBuffer(emissiveMaterialVertsBuffers, base.materialVertsBuffers, "verts", i);

                    Graphics.DrawMesh(base.mesh, Matrix4x4.identity, emissiveMats[i], 0, null, i, null, false, false);
                }
            }

            private void UpdateBuffer(Dictionary<int, ComputeBuffer> ours, Dictionary<int, ComputeBuffer> theirs, string buf, int i)
            {
                ComputeBuffer ourB, theirB;
                ours.TryGetValue(i, out ourB);
                theirs.TryGetValue(i, out theirB);
                if (ourB != theirB) {
                    emissiveMats[i].SetBuffer(buf, theirB);
                    ours[i] = theirB;
                }
            }
        }
    }
}