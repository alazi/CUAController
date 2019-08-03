﻿namespace CUAControllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.UI;
    using System.Text.RegularExpressions;
    using SimpleJSON;

    class CUAControllers : MVRScript
    {
        protected JSONStorableString jsonNodeRE;
        private JSONStorableBool debug;
        bool haveDoneRestore;
        bool wasFromReload;
        private Regex regex;

        private IEnumerator WaitFor(Func<bool> test, float timeoutSeconds = 3.0f, float delay = 0.2f)
        {
            float waited = 0;
            while (!test() && waited <= timeoutSeconds) {
                waited += delay;
                yield return new WaitForSeconds(delay);
            }
        }

        protected IEnumerator RestoreFromLoad()
        {
            yield return WaitFor(() => containingAtom.reParentObject.Find("object/rescaleObject").GetComponentsInChildren<Rigidbody>().Length > 0);
            foreach (var t in GetControlTargets()) {
                yield return WaitFor(() => GetAtomById(t.controlName) != null);
            }
            yield return SyncCO();
        }

        public override void RestoreFromJSON(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true, JSONArray presetAtoms = null, bool setMissingToDefault = true)
        {
            wasFromReload = false;
            base.RestoreFromJSON(jc, restorePhysical, restoreAppearance, presetAtoms, setMissingToDefault);
            StartCoroutine(RestoreFromLoad());
        }

        private void DestroyOldAtoms()
        {
            if (insideRestore) return;
            foreach (var item in SuperController.singleton.GetAtoms()) {
                if (item.uid.StartsWith(baseName)) {
                    SuperController.LogMessage($"deleting {item.uid}");
                    SuperController.singleton.RemoveAtom(item);
                }
            }
        }

        private void OnDestroy()
        {
            DestroyOldAtoms();
        }
        private IEnumerator MaybeWasReload()
        {
            yield return null;
            if (wasFromReload) {
                DestroyOldAtoms();
                yield return SyncCO();
            }
        }

        // Use this for initialization
        public override void Init()
        {
            UIStringMessage("Target Nodes (regex):", false);
            StringTextbox(ref jsonNodeRE, "target nodes re", ".*", _ => { regex = new Regex(jsonNodeRE.val); }, true);
            BoolCheckbox(ref debug, "Debug", false, _ => Sync(), false);

            CreateButton("Rebuild").button.onClick.AddListener(() => { DestroyOldAtoms(); Sync(); });

            regex = new Regex(jsonNodeRE.val);
            
            // do a rebuild if we came from an initial setup or the reload button, but not if we're in scene load
            wasFromReload = true;
            StartCoroutine(MaybeWasReload());
        }
        const string atomType = "Sphere";
        const string targetGO = "object/rescaleObject/Sphere";
        private IEnumerator SetupControlAtom(string name)
        {
            var controlAtom = GetAtomById(name);
            if (controlAtom && controlAtom.type != atomType) {
                SuperController.singleton.RemoveAtom(controlAtom);
                controlAtom = null;
            }
            if (!controlAtom) {
                yield return SuperController.singleton.AddAtomByType(atomType, name);
                controlAtom = SuperController.singleton.GetAtomByUid(name);
                controlAtom.parentAtom = containingAtom;
                controlAtom.collisionEnabledJSON.val = false; // don't explode while we're reconnecting on load
                controlAtom.mainController.enableSelectRoot = true;
                controlAtom.mainController.InitUI();
            }
        }

        private GameObject CreateControlLink(string rbName)
        {
            var links = containingAtom.transform.Find("ControlLinks");
            if (!links) {
                links = new GameObject("ControlLinks").transform;
                links.parent = containingAtom.transform;
            }
            var name = $"${rbName}Link";
            var link = links.Find(name)?.gameObject;
            if (link)
                return link;
            link = new GameObject(name);
            link.transform.parent = links;
            return link;
        }

        private IEnumerable<ControlTarget> GetControlTargets()
        {
            var rescale = containingAtom.reParentObject.Find("object/rescaleObject");
            foreach (var rb in rescale.GetComponentsInChildren<Rigidbody>()) {
                if (!regex.IsMatch(rb.name))
                    continue;
                var controlName = $"{baseName}{rb.name}::Control";
                yield return new ControlTarget() { rb = rb, controlName = controlName };
            }
        }
        private void Sync()
        {
            if (insideRestore) return;
            StartCoroutine(SyncCO());
        }
        private string baseName => $"z${containingAtom.uid}::";
        private IEnumerator SyncCO()
        {
            while (containingAtom.transform.Find("ControlLinks"))
                DestroyImmediate(containingAtom.transform.Find("ControlLinks")?.gameObject);

            foreach (var t in GetControlTargets()) {
                var rb = t.rb; var controlName = t.controlName;
                yield return SetupControlAtom(controlName);
                var controlAtom = GetAtomById(controlName);

                controlAtom.reParentObject.position = rb.position;
                controlAtom.reParentObject.rotation = rb.rotation;

                var linkGO = CreateControlLink(rb.name);
                var linkRB = linkGO.AddComponent<Rigidbody>();
                linkRB.transform.position = rb.transform.position;
                linkRB.transform.rotation = rb.transform.rotation;
                linkRB.mass = 0.1f;
                linkRB.drag = 0;
                linkRB.angularDrag = 0;
                linkRB.useGravity = false;
                linkRB.isKinematic = false;

                var controlGO = controlAtom.reParentObject.Find(targetGO);

                CreateFixedJoint(linkGO, controlAtom.reParentObject.Find("object").GetComponent<Rigidbody>());
                CreateFixedJoint(linkGO, rb);

                if (debug.val) {
                    linkGO.AddComponent<DebugComponent>();
                    controlGO.localScale = new Vector3(0.05f, 0.05f, 0.05f);
                    controlGO.GetComponent<MeshRenderer>().enabled = true;
                } else {
                    Destroy(linkGO.GetComponent<DebugComponent>());
                    controlGO.GetComponent<MeshRenderer>().enabled = false;
                }
            }
        }

        private void CreateFixedJoint(GameObject linkGO, Rigidbody target)
        {
            var joint = linkGO.AddComponent<FixedJoint>();
            joint.autoConfigureConnectedAnchor = false;
            joint.connectedBody = target;
            joint.connectedAnchor = new Vector3();
        }

        // UI helpers

        private void FloatSlider(ref JSONStorableFloat output, string name, float start, JSONStorableFloat.SetFloatCallback callback, float min, float max, bool rhs)
        {
            output = new JSONStorableFloat(name, start, callback, min, max, false, true);
            RegisterFloat(output);
            CreateSlider(output, rhs);
        }

        private void BoolCheckbox(ref JSONStorableBool output, string name, bool start, JSONStorableBool.SetBoolCallback callback, bool rhs)
        {
            output = new JSONStorableBool(name, start, callback);
            RegisterBool(output);
            CreateToggle(output, rhs);
        }
        private void UIStringMessage(string message, bool rhs)
        {
            CreateTextField(new JSONStorableString("", message), rhs);
        }
        private void StringTextbox(ref JSONStorableString output, string name, string start, JSONStorableString.SetStringCallback callback, bool rhs)
        {
            output = new JSONStorableString(name, start, callback);

            RegisterString(output);
            var textfield = CreateTextField(output, rhs);
            var input = textfield.gameObject.AddComponent<InputField>();
            input.textComponent = textfield.UItext;
            textfield.backgroundColor = Color.white;
            output.inputField = input;
        }

        class ControlTarget
        {
            public Rigidbody rb;
            public string controlName;
        }
    }




    // Debug helpers
    class DebugComponent : MonoBehaviour
    {


        static Material lineMaterial;
        static void CreateLineMaterial()
        {
            if (!lineMaterial) {
                // Unity has a built-in shader that is useful for drawing
                // simple colored things.
                Shader shader = Shader.Find("Hidden/Internal-Colored"); //
                                                                        //lineMaterial = new Material("Shader \"Lines/Colored Blended\" {" + "SubShader { Pass { " + "    Blend SrcAlpha OneMinusSrcAlpha " + "    ZWrite Off ZTest Off Cull Off Fog { Mode Off } " + "    BindChannels {" + "      Bind \"vertex\", vertex Bind \"color\", color }" + "} } }");
                lineMaterial = new Material(shader);
                //new Material(shader);
                lineMaterial.hideFlags = HideFlags.HideAndDontSave;
                // Turn on alpha blending
                lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                // Turn backface culling off
                lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                // Turn off depth writes
                lineMaterial.SetInt("_ZWrite", 0);
                lineMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
        }

        // Will be called after all regular rendering is done
        public void OnRenderObject()
        {
            CreateLineMaterial();
            // Apply the line material
            lineMaterial.SetPass(0);

            GL.PushMatrix();
            // Set transformation matrix for drawing to
            // match our transform
            //GL.MultMatrix(transform.localToWorldMatrix);

            // Draw lines
            GL.Begin(GL.LINES);
            try {
                float i = 0;
                foreach (var item in base.gameObject.GetComponents<Joint>()) {
                    if (!item.connectedBody) continue;
                    // One vertex at transform position
                    GL.Color(new Color(0, 1, i/2, 0.8F));
                    GL.Vertex(transform.position);
                    GL.Color(new Color(1, 0, i/2, 0.8F));
                    GL.Vertex(item.connectedBody.transform.position);
                    i++;
                }
            } finally {

                //GL.Vertex(new Vector3());
                //GL.Vertex(transform.position);
                // Another vertex at edge of circle

                GL.End();
                GL.PopMatrix();
            }
        }


        string printIt(Transform f, string depth)
        {
            string ret = "";
            if (f == null) return "";
            ret += depth + f.name + " " + f.gameObject.activeInHierarchy + " " + f.gameObject.activeSelf + "\n";
            var x = f.GetComponent<Rigidbody>()?.inertiaTensor;
            if (x != null) ret += (depth + x.ToString()) + "\n";
            x = f.GetComponent<Rigidbody>()?.centerOfMass;
            if (x != null) ret += (depth + x.ToString()) + "\n";
            foreach (var comp in f.GetComponents<Component>()) {
                ret += (depth + comp.ToString()) + "\n";
            }
            foreach (var item2 in f) {
                ret += printIt((Transform)item2, depth + " ");
            }
            return ret;
        }
        static public GameObject getChildGameObject(GameObject fromGameObject, string withName)
        {
            var ts = fromGameObject.transform.GetComponentsInChildren<Transform>();
            foreach (Transform t in ts) if (t.gameObject.name == withName) return t.gameObject;
            return null;
        }
    }


}
