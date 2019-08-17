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

    class CUAPoser : MVRScript
    {
        bool wasFromReload;
        JSONStorableStringChooser morphAtom;
        JSONStorableStringChooser morphName;
        DAZMorph morph;
        private JSONStorableFloat amount;
        private JSONStorableFloat springMult;

        private IEnumerator WaitFor(Func<bool> test, float timeoutSeconds = 3.0f, float delay = 0.2f)
        {
            float waited = 0;
            while (!test() && waited <= timeoutSeconds) {
                waited += delay;
                yield return new WaitForSeconds(delay);
            }
        }

        void ReloadMorphs()
        {
            var atom = GetAtomById(morphAtom.val);
            var banks = atom.gameObject.GetComponentsInChildren<DAZMorphBank>();
            morphName.choices = banks.SelectMany(b => b.morphs).Where(m => m.group == "Pose Controls").Select(m => m.displayName).ToList();
        }
        Dictionary<string, ApplyMorph> joints;
        void SetMorph()
        {
            var atom = GetAtomById(morphAtom.val);
            var banks = atom.gameObject.GetComponentsInChildren<DAZMorphBank>();
            
            try {
                morph = banks.SelectMany(b => b.morphs).Where(m => m.displayName == morphName.val).Single();
            } catch (Exception e) {
                SuperController.LogError($"Morph {morphName.val} not found or other issue: {e}");
            }
            joints = containingAtom.reParentObject.GetComponentsInChildren<ConfigurableJoint>().
                Select(j => j.gameObject.AddComponent<ApplyMorph>()).ToDictionary(j => j.name);

            
        }


        // Use this for initialization
        public override void Init()
        {
            GetAtomUIDs();
            StringDropdown(ref morphAtom, "Atom for morph", "Person", GetAtomUIDs(), _ => ReloadMorphs(), false);
            FloatSlider(ref springMult, "Spring Strength", 1, _ => UpdateSpring(), 0, 10, true);
            StringDropdown(ref morphName, "morph", "Tail Bend", new List<string>(), _ => SetMorph(), false);
            FloatSlider(ref amount, "Morph Strength", 0, _ => UpdateMorph(), -1, 1, true);
            ReloadMorphs();
            SetMorph();
        }

        private void UpdateMorph()
        {
            foreach (var item in containingAtom.reParentObject.GetComponentsInChildren<ApplyMorph>()) {
                item.addRotation = Vector3.zero;
            }
            foreach (var f in morph.formulas) {
                Vector3 v;
                
                switch (f.targetType) {
                    case DAZMorphFormulaTargetType.RotationX:
                        v = new Vector3(1, 0, 0);
                        break;
                    case DAZMorphFormulaTargetType.RotationY:
                        v = new Vector3(0, 1, 0);
                        break;
                    case DAZMorphFormulaTargetType.RotationZ:
                        v = new Vector3(0, 0, 1);
                        break;
                    default:
                        continue;
                }
                ApplyMorph joint;
                if (joints.TryGetValue(f.target, out joint)) {
                    v *= amount.val * f.multiplier;
                    //SuperController.LogMessage($"'{f.target}' '{f.targetType}' {joints.ContainsKey(f.target)} {v}");
                    joint.addRotation += v;
                }
            }
            foreach (var item in containingAtom.reParentObject.GetComponentsInChildren<ApplyMorph>()) {
                item.Apply();
                //SuperController.LogMessage($"'{item}' '{item.addRotation}' {item.origRotation} =>  {item.GetComponent<ConfigurableJoint>().targetRotation}");
            }
        }
        private void UpdateSpring()
        {
            foreach (var item in joints) {
                item.Value.SetSpring(springMult.val);
            }
        }
        class ApplyMorph : MonoBehaviour
        {
            public Quaternion origRotation;
            public Vector3 addRotation;
            ConfigurableJoint joint;
            float origSpring;
            void Awake()
            {
                joint = GetComponent<ConfigurableJoint>();
                if (joint) {
                    origRotation = joint.targetRotation;
                    origSpring = joint.slerpDrive.positionSpring;
                }
            }
            public void Apply()
            {
                if (joint)
                    joint.targetRotation = origRotation * Quaternion.Euler(addRotation);
            }
            public void SetSpring(float mult)
            {
                if (joint) {
                    var d = joint.slerpDrive;
                    d.positionSpring = origSpring * mult;
                    joint.slerpDrive = d;
                }
            }
            void OnDestroy()
            {
                if (joint) {
                    joint.targetRotation = origRotation;
                    SetSpring(1);
                }
            }
        }

        void OnDestroy()
        {
            foreach (var item in containingAtom.reParentObject.GetComponentsInChildren<ApplyMorph>()) {
                Destroy(item);
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
        private void StringDropdown(ref JSONStorableStringChooser output, string name, string start, List<String> choices, JSONStorableStringChooser.SetStringCallback callback, bool rhs)
        {
            output = new JSONStorableStringChooser(name, choices, start, name, callback);

            RegisterStringChooser(output);
            CreateScrollablePopup(output, rhs);
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