using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using SimpleJSON;
using System.Linq;

/*
 * UnityAssetVamifier is a simple plugin to convert Materials used in
 * Unity Asset Bundles (.assetbundle) from Unity default shaders
 * (Roughness setup preferred) to the builtin VAM shader and expose
 * the shader properties in the UI.
 *
 * Version: 1.1
 * Changes:
 * - Improved waiting for Asset Bundle solution
 * - Reordered UI to match VAM UI (sliders left, colors right)
 * 
 * Authors: NoStage3
 * License: Creative Commons with Attribution (CC BY 3.0)
 */

namespace MVRPlugin
{
    public class UnityAssetVamifier : MVRScript
    {
        private readonly List<string> UNITY_SHADER_NAMES = new List<string>(new string[] {
            "Standard",
            "Standard (Specular setup)",
            "Standard (Roughness setup)"
        });
        private readonly List<string> INVALID_SHADER_KEYWORDS = new List<string>(new string[] {
            "_ALPHAPREMULTIPLY_ON"
        });
        private readonly string VAM_SHADER_NAME = "Custom/Subsurface/GlossNMCull";

        protected UIDynamicSlider specIntensitySlider;
        protected UIDynamicSlider specFresnelSlider;
        protected UIDynamicSlider specSharpnessSlider;
        protected UIDynamicSlider diffOffsetSlider;
        protected UIDynamicSlider specOffsetSlider;
        protected UIDynamicSlider glossOffsetSlider;
        protected UIDynamicSlider iBLFilterSlider;

        protected JSONStorableColor jDiffColor;
        protected JSONStorableColor jSpecColor;
        protected JSONStorableFloat jSpecIntensityFloat;
        protected JSONStorableFloat jSpecSharpnessFloat;
        protected JSONStorableFloat jSpecFresnelFloat;
        protected JSONStorableFloat jDiffOffsetFloat;
        protected JSONStorableFloat jSpecOffsetFloat;
        protected JSONStorableFloat jGlossOffsetFloat;
        protected JSONStorableFloat jIBLFilterFloat;
        protected JSONStorableColor jSubdermisColor;

        protected List<Material[]> origMaterials = new List<Material[]>();
        protected List<Material[]> vamMaterials = new List<Material[]>();
        protected Shader vamPropShader;
        protected List<Renderer> renderers = new List<Renderer>();

        public override void Init()
        {
            try {

                // create new VAM material from built-in shader
                vamPropShader = Shader.Find(VAM_SHADER_NAME);

                // build/load/populate material controls for VAM UI
                BuildUIControls();

                // since there is no "asset bundle loaded" callback yet, we have to wait
                StartCoroutine(WaitForAssetBundle());

            } catch (Exception e) {
                SuperController.LogError("Exception caught: " + e);
            }
        }


        protected void BuildUIControls()
        {

            //Diff Color
            HSVColor diffColorHSVC = HSVColorPicker.RGBToHSV(1f, 1f, 1f);
            jDiffColor = new JSONStorableColor("DiffuseColor", diffColorHSVC, SetDiffColor);
            RegisterColor(jDiffColor);
            CreateColorPicker(jDiffColor, true);

            //Specular Color
            HSVColor specColorHSVC = HSVColorPicker.RGBToHSV(1f, 1f, 1f);
            jSpecColor = new JSONStorableColor("SpecularColor", specColorHSVC, SetSpecColor);
            RegisterColor(jSpecColor);
            CreateColorPicker(jSpecColor, true);

            // Specular Intensity
            jSpecIntensityFloat = new JSONStorableFloat("SpecularIntensity", 0.5f, SetSpecIntensity, 0f, 1f, true);
            RegisterFloat(jSpecIntensityFloat);
            specIntensitySlider = CreateSlider(jSpecIntensityFloat);

            // Specular Sharpness
            jSpecSharpnessFloat = new JSONStorableFloat("SpecularSharpness", 6f, SetSpecSharpness, 0f, 10f, true);
            RegisterFloat(jSpecSharpnessFloat);
            specSharpnessSlider = CreateSlider(jSpecSharpnessFloat);

            // Specular Fresnel
            jSpecFresnelFloat = new JSONStorableFloat("SpecularFresnel", 0f, SetSpecFresnel, 0f, 1f, true);
            RegisterFloat(jSpecFresnelFloat);
            specFresnelSlider = CreateSlider(jSpecFresnelFloat);

            // Diffuse Offset
            jDiffOffsetFloat = new JSONStorableFloat("DiffuseOffset", 0f, SetDiffOffset, -1f, 1f, true);
            RegisterFloat(jDiffOffsetFloat);
            diffOffsetSlider = CreateSlider(jDiffOffsetFloat);

            // Spec Offset
            jSpecOffsetFloat = new JSONStorableFloat("SpecularOffset", 0f, SetSpecOffset, -1f, 1f, true);
            RegisterFloat(jSpecOffsetFloat);
            specOffsetSlider = CreateSlider(jSpecOffsetFloat);

            // Gloss Offset
            jGlossOffsetFloat = new JSONStorableFloat("GlossOffset", 0.8f, SetGlossOffset, 0, 1f, true);
            RegisterFloat(jGlossOffsetFloat);
            glossOffsetSlider = CreateSlider(jGlossOffsetFloat);

            // IBL Filter (affects Global Illum Skybox ?!)
            jIBLFilterFloat = new JSONStorableFloat("IBLFilter", 0f, SetIBLFilter, 0, 1f, true);
            RegisterFloat(jIBLFilterFloat);
            iBLFilterSlider = CreateSlider(jIBLFilterFloat);

            //Subdermis Color
            HSVColor subdermisColorHSVC = HSVColorPicker.RGBToHSV(1f, 1f, 1f);
            jSubdermisColor = new JSONStorableColor("SubdermisColor", subdermisColorHSVC, SetSubdermisColor);
            RegisterColor(jSubdermisColor);
            CreateColorPicker(jSubdermisColor, true);

        }


        protected bool IsValidMaterial(Material mat)
        {
            if (!UNITY_SHADER_NAMES.Contains(mat.shader.name))
                return false;

            foreach (string keyword in INVALID_SHADER_KEYWORDS) {
                if (mat.IsKeywordEnabled(keyword))
                    return false;
            }
            return true;
        }


        // try to get the renderers of the loaded asset bundle for 3 seconds.
        protected IEnumerator WaitForAssetBundle()
        {
            List<Renderer> allRenderers = new List<Renderer>();

            float waitForSeconds = 3.0f;
            float waited = 0f;
            while (allRenderers.Count < 1 && waited <= waitForSeconds) {
                containingAtom.reParentObject.GetComponentsInChildren<Renderer>(false, allRenderers);
                waited += 0.2f;
                yield return new WaitForSeconds(0.2f);
            }

            ConvertMaterial(allRenderers);
        }


        protected void ConvertMaterial(List<Renderer> allRenderers)
        {
            List<Material> invalidMaterials = new List<Material>();

            if (allRenderers.Count == 0) {
                SuperController.LogMessage("Make sure you have an .assetbundle loaded and a prop selected from the Asset dropdown. Then reload this plugin.");
                return;
            }

            // filter renderers by supported materials
            foreach (Renderer renderer in allRenderers) {
                //SuperController.LogError(renderer.material.shader.name);
                var origMats = renderer.materials;
                var vamMats = new List<Material>();
                foreach (var mat in origMats) {
                    if (IsValidMaterial(mat)) {
                        vamMats.Add(new Material(vamPropShader));
                    } else {
                        vamMats.Add(null);
                        invalidMaterials.Add(mat);
                    }
                }
                if (vamMats.Count > 0) {
                    renderers.Add(renderer);
                    origMaterials.Add(origMats);
                    vamMaterials.Add(vamMats.ToArray());
                }

            }

            // output invalid/skipped materials
            foreach (Material invalidMaterial in invalidMaterials) {
                SuperController.LogMessage("Material skipped: " + invalidMaterial.name + " -- Shader: [" + invalidMaterial.shader.name + "]");
            }



            if (renderers.Count == 0) {
                SuperController.LogError("No compatible material could be found on this asset. Check message log for skipped materials/shaders.");
                return;
            }


            for (int i = 0; i < renderers.Count; i++) {
                var renderer = renderers[i];
                var materials = renderers[i].materials;
                for (int j = 0; j < renderer.materials.Length; j++) {
                    if (vamMaterials[i][j] == null) continue;
                    // reassign textures from unity to vam material
                    vamMaterials[i][j].name = origMaterials[i][j].name + "vamified";
                    vamMaterials[i][j].SetTexture("_MainTex", origMaterials[i][j].GetTexture("_MainTex"));
                    vamMaterials[i][j].SetTexture("_BumpMap", origMaterials[i][j].GetTexture("_BumpMap"));
                    try { vamMaterials[i][j].SetTexture("_SpecTex", origMaterials[i][j].GetTexture("_SpecGlossMap")); } catch { }
                    try { vamMaterials[i][j].SetTexture("_GlossTex", origMaterials[i][j].GetTexture("_MetallicGlossMap")); } catch { }
                    // assign VAM material to prop renderer
                    materials[j] = vamMaterials[i][j];
                }
                renderers[i].materials = materials;
            }
            SuperController.LogMessage($"total {vamMaterials.SelectMany(x => x).Where(x => x != null).Count()}");

            // apply values initially
            SetDiffColor(jDiffColor);
            SetSpecColor(jSpecColor);
            SetSpecIntensity(jSpecIntensityFloat);
            SetSpecSharpness(jSpecSharpnessFloat);
            SetSpecFresnel(jSpecFresnelFloat);
            SetDiffOffset(jDiffOffsetFloat);
            SetSpecOffset(jSpecOffsetFloat);
            SetGlossOffset(jGlossOffsetFloat);
            SetSubdermisColor(jSubdermisColor);

        }


        void OnDestroy()
        {
            // restore original materials
            for (int i = 0; i < renderers.Count; i++) {
                var renderer = renderers[i];

                for (int j = 0; j < renderer.materials.Length; j++) {
                    if (vamMaterials[i][j] == null) continue;
                    renderers[i].materials[j] = origMaterials[i][j];
                }
            }
        }


        protected void SetDiffColor(JSONStorableColor jcolor)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                SuperController.LogMessage($"Setcolor {vamMaterial} from {vamMaterial.GetColor("_Color")} to {jcolor.colorPicker.currentColor}");
                vamMaterial.SetColor("_Color", jcolor.colorPicker.currentColor);
            }
        }

        protected void SetSpecColor(JSONStorableColor jcolor)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetColor("_SpecColor", jcolor.colorPicker.currentColor);
            }
        }

        protected void SetSpecIntensity(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_SpecInt", jf.val);
            }
        }

        protected void SetSpecFresnel(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_Fresnel", jf.val);
            }
        }

        protected void SetSpecSharpness(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_Shininess", jf.val);
            }
        }

        protected void SetDiffOffset(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_DiffOffset", jf.val);
            }
        }

        protected void SetSpecOffset(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_SpecOffset", jf.val);
            }
        }

        protected void SetGlossOffset(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_GlossOffset", jf.val);
            }
        }

        protected void SetIBLFilter(JSONStorableFloat jf)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetFloat("_IBLFilter", jf.val);
            }
        }

        protected void SetSubdermisColor(JSONStorableColor jcolor)
        {
            foreach (Material vamMaterial in vamMaterials.SelectMany(x=>x).Where(x=>x!=null)) {
                vamMaterial.SetColor("_SubdermisColor", jcolor.colorPicker.currentColor);
            }
        }

    }
}