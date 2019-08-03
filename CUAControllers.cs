namespace CUAControllers
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using UnityEngine;
    using UnityEngine.UI;
    using System.Text.RegularExpressions;
    using SimpleJSON;

    class CUAControllers : MVRScript
    {
        private JSONStorableString jsonNodeRE;
        private JSONStorableBool debug;
        private JSONStorableFloat massScale;
        bool haveDoneRestore;
        bool wasFromReload;
        private Regex regex;

        private string _cachedBaseName;
        private string baseName {
            get {
                // if you remove the script, containingAtom gets cleared before OnDestroy
                if (containingAtom != null) { 
                    _cachedBaseName = $"z${containingAtom.uid}::";
                }
                return _cachedBaseName;
            }
        }

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

        private void ModifyFCs(Action<FreeControllerV3> act)
        {
            foreach (var t in GetControlTargets()) {
                var atom = GetAtomById(t.controlName);
                act(atom.mainController);
            }
        }

        // Use this for initialization
        public override void Init()
        {
            UIStringMessage("Target Nodes (regex):", false);
            StringTextbox(ref jsonNodeRE, "target nodes re", ".*", _ => { regex = new Regex(jsonNodeRE.val); }, true);
            BoolCheckbox(ref debug, "Debug", false, _ => Sync(), true);

            Button("Rebuild", () => { DestroyOldAtoms(); Sync(); }, true);
            FloatSlider(ref massScale, "mass scale", 0.1f, _ => Sync(), 0.001f, 10, true);

            Button("All Nodes: Control Off", () => ModifyFCs(fc => {
                fc.currentPositionState = FreeControllerV3.PositionState.Off;
                fc.currentRotationState = FreeControllerV3.RotationState.Off;
            }), false);
            Button("All Nodes: Control On", () => ModifyFCs(fc => {
                fc.currentPositionState = FreeControllerV3.PositionState.On;
                fc.currentRotationState = FreeControllerV3.RotationState.On;
            }), false);
            Button("All Nodes: Control Rotation", () => ModifyFCs(fc => {
                fc.currentPositionState = FreeControllerV3.PositionState.Off;
                fc.currentRotationState = FreeControllerV3.RotationState.On;
            }), false);
            Button("All Nodes: Control Position", () => ModifyFCs(fc => {
                fc.currentPositionState = FreeControllerV3.PositionState.On;
                fc.currentRotationState = FreeControllerV3.RotationState.Off;
            }), false);
            Button("All Nodes: Comply", () => ModifyFCs(fc => {
                fc.currentPositionState = FreeControllerV3.PositionState.Comply;
                fc.currentRotationState = FreeControllerV3.RotationState.Comply;
            }), false);
            regex = new Regex(jsonNodeRE.val);
            
            // do a rebuild if we came from an initial setup or the reload button, but not if we're in scene load
            wasFromReload = true;
            StartCoroutine(MaybeWasReload());
        }
        const string atomType = "Sphere";
        const string renderGOName = "object/rescaleObject/Sphere";
        private IEnumerator SetupControlAtom(string name, Rigidbody rb)
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

                // Unfortunately, the mass*scale needs to be similar for it to behave well,
                // despite being an infinite-strength FixedJoint, and massScale only trades some annoyances for others.
                controlAtom.mainController.RBMass = rb.mass * massScale.val;
                controlAtom.mainController.RBDrag = 0;
                controlAtom.mainController.RBAngularDrag = 0;
            }
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
        
        private IEnumerator SyncCO()
        {

            foreach (var t in GetControlTargets()) {
                var rb = t.rb; var controlName = t.controlName;
                yield return SetupControlAtom(controlName, t.rb);
                var controlAtom = GetAtomById(controlName);

                controlAtom.reParentObject.position = rb.position;
                controlAtom.reParentObject.rotation = rb.rotation;

                var controlRB = controlAtom.mainController.followWhenOffRB;
                var joint = rb.GetComponents<FixedJoint>().Where(j => j.connectedBody == null || j.connectedBody == controlRB).FirstOrDefault();
                if (!joint) {
                    joint = rb.gameObject.AddComponent<FixedJoint>();
                }

                joint.autoConfigureConnectedAnchor = false;
                joint.connectedBody = controlRB;
                joint.connectedAnchor = new Vector3();
                joint.connectedMassScale = massScale.val;

                var controlGO = controlAtom.reParentObject.Find(renderGOName);

                if (debug.val) {
                    controlGO.localScale = new Vector3(0.05f, 0.05f, 0.05f);
                    controlGO.GetComponent<MeshRenderer>().enabled = true;
                } else {
                    controlGO.GetComponent<MeshRenderer>().enabled = false;
                }
            }
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
        private void Button(string label, UnityEngine.Events.UnityAction handler, bool rhs)
        {
            CreateButton(label, rhs).button.onClick.AddListener(handler);
        }

        class ControlTarget
        {
            public Rigidbody rb;
            public string controlName;
        }
    }
}
