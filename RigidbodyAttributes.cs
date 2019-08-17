using UnityEngine;

/* This is a dummy script, you should put it in the root of your Unity projects 
 * Assets path, and should _not_ include the resulting Assembly-CSharp.dll with 
 * your assetbundle.
 * 
 * You can add this component in the Unity editor and set properties on it, and
 * then when you load your prefab in VaM it'll link up to the actual VaM-provided
 * component code that actually does things.
 * 
 * In particular, add this to any GameObjects where you have a Rigidbody with an 
 * attached collider, and then hit the Use Override Tensor checkbox. This will keep 
 * joint behavior from changing (and often breaking entirely) with the collider.
 */

[ExecuteInEditMode]
public class RigidbodyAttributes : MonoBehaviour
{

    public bool _useOverrideIterations;
    public bool _useInterpolation;

    public int _solverIterations;

    public bool _useOverrideTensor = true;
    public Vector3 _inertiaTensor = Vector3.one;
    public int _solverVelocityIterations;

    private void Sync()
    {
        var rb = GetComponent<Rigidbody>();
        if (!rb) return;
        if (_useOverrideTensor && isActiveAndEnabled) {
            rb.inertiaTensor = _inertiaTensor;
        } else {
            rb.ResetInertiaTensor();
        }
    }
    private void Awake()
    {
        Sync();
    }
    private void OnEnable()
    {
        Sync();
    }
    private void OnDisable()
    {
        Sync();
    }
}
