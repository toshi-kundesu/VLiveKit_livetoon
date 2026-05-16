// VLiveKit is all Unlicense.
// unlicense: https://unlicense.org/
// this comment & namespace can be removed.
// last update: 2025/05/18
using UnityEngine;
using System.Linq;

/// <summary>
/// head.forward / head.up を送信し、Scene で Gizmo 表示するコンポーネント
/// </summary>
[ExecuteAlways]
public class FaceDirectionSender : MonoBehaviour
{
    [Header("参照")]
    public Transform head;

    [Tooltip("Face 用マテリアルを直接指定（インスタンス専用推奨）")]
    public Material[] targetMaterials;

    [Header("カスタム方向 (任意)")]
    public bool overrideDirection = false;
    public Vector3 customForward = Vector3.forward;
    public Vector3 customUp      = Vector3.up;

    [Header("Gizmo 設定")]
    [Min(0.01f)]
    public float gizmoLength = 0.3f;      // ← 線の長さ
    public Color forwardColor = Color.blue;
    public Color upColor      = Color.green;

    [Header("Face 用パラメータ")]
    [Range(0, 1)]
    public float faceSphereIntensity = 0.5f;
    // head position offset
    public Vector3 headPositionOffset = Vector3.zero;

    // ───────────────────────────────────────
    void LateUpdate()
    {
        if (head == null || targetMaterials == null || targetMaterials.Length == 0) return;

        Vector3 fwd = (overrideDirection ? head.rotation * customForward : head.forward).normalized;
        Vector3 up  = (overrideDirection ? head.rotation * customUp      : head.up).normalized;
        var renderers = FindSceneObjects<Renderer>();

    
foreach (var mat in targetMaterials.Where(m => m != null))
{
    var renderer = renderers
                   .FirstOrDefault(r => r.sharedMaterial == mat ||
                                        r.sharedMaterials.Contains(mat));
    if (renderer == null) continue;

    // 頭の位置（ワールド）→ そのレンダラーのローカル空間へ変換
    Vector3 localHeadPos = renderer.transform.InverseTransformPoint(
                               head.position + headPositionOffset);

    // マテリアルか MaterialPropertyBlock かは運用に合わせて
    mat.SetVector("_FacePosition", localHeadPos);

    // 方向ベクトルはワールドのままでも OK（後述）
    mat.SetVector("_FaceForwardDirection", fwd);
    mat.SetVector("_FaceUpDirection",      up);

    mat.SetInt   ("_isFace", 1);
    mat.SetFloat ("_FaceSphereIntensity", faceSphereIntensity);
}

    }

    // ───────────────────────────────────────
    static T[] FindSceneObjects<T>() where T : Object
    {
#if UNITY_2022_2_OR_NEWER || UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }

    void OnDrawGizmos()
    {
        if (head == null) return;

        Vector3 pos = head.position + headPositionOffset;
        Vector3 fwd = (overrideDirection ? head.rotation * customForward : head.forward).normalized;
        Vector3 upV = (overrideDirection ? head.rotation * customUp      : head.up).normalized;

        float len = gizmoLength;
        float sphereRadius = len * 0.1f;

        Gizmos.color = forwardColor;
        Gizmos.DrawLine(pos, pos + fwd * len);
        Gizmos.DrawSphere(pos + fwd * len, sphereRadius);

        Gizmos.color = upColor;
        Gizmos.DrawLine(pos, pos + upV * len);
        Gizmos.DrawSphere(pos + upV * len, sphereRadius);
    }
}
