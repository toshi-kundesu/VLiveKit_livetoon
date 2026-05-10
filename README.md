## 概要

VLiveKitの一部として開発している、  
ライブ制作向けキャラクターシェーダーです。

実際のライブ制作で使用しながら改善しているもので、  
現在は整理およびリファクタリングを進めています。

---

## 実装済み機能（整理中）

- 法線の球面化によるライティングの安定化
- lit / shade 境界の彩度制御による表面下散乱風表現
- 視野角依存の見え方を補正するジオメトリシェーディング
- 前髪の落ち影を整えるためのカスタムライト
- HDRP対応（HDRP向けToonシェーダーとして動作）

---

## 特徴

- 複数ライト環境でもルックが崩れにくい設計
- ライブ用途を前提とした安定した描画

他のポストプロセスと組み合わせることで表現の幅が広がります：

- Diffusion
- キャラクターブルーム
- 原神ライクなポストプロセス

（※ これらは別パッケージに含まれます）

---

## 今後の予定

- 汗などのライブ向け表現の追加
- キャラクター表現の拡張
- 全体的なリファクタリング

---

## インストール

`Packages/manifest.json` の `dependencies` に以下を追加してください。

```json
{
  "dependencies": {
    "com.toshi.vlivekit.livetoon": "https://github.com/toshi-kundesu/VLiveKit_livetoon.git?path=/Assets/toshi.VLiveKit/livetoon#main"
  }
}
```

---

## LiveToon Shader Converter

Open the converter from `toshi/VLiveKit/LiveToon/Shader Converter`.

The converter currently uses a non-destructive LoadModel-style baseline for VRM 0.x / MToon materials:

- use the Editor Window for one-off conversion, or add `LiveToonShaderConverter` to a scene object to save conversion settings per scene
- the `LiveToonShaderConverter` component exposes the same convert and restore buttons in its Inspector
- creates fresh LiveToon material copies on every conversion run and assigns those copies to the selected model renderers
- keeps the original source material assets unchanged
- stores copies in a `LiveToonMaterials` folder next to source materials under `Assets/`
- stores copies under `Assets/VLiveKitGenerated/LiveToonMaterials` when the source material is in a package or is not an asset
- records the original material asset path on each generated copy so `Restore Original Material Assignments` can put renderer slots back, even when several conversion generations exist
- swaps the copied material shader to `toshi/VLiveKit/livetoon`
- preserves existing MToon-compatible material properties such as `_CullMode` and `_BlendMode`
- preserves `_Color`, including alpha, so MToon Transparent opacity is carried into LiveToon
- fills `_ShadeTexture` from `_MainTex` only when `_ShadeTexture` is empty
- rebuilds `_SrcBlend`, `_DstBlend`, `_ZWrite`, `_AlphaToMask`, and `renderQueue` from `_BlendMode`: Opaque `2225`, Cutout `2450`, TransparentWithZWrite `2501`, Transparent `3000`
- restores the material `RenderType` tag and alpha keywords from `_BlendMode` after the shader swap
- restores MToon outline width/color keywords from `_OutlineWidthMode` and `_OutlineColorMode` after the shader swap
- disables LiveToon's legacy HDRP `GBuffer` pass and renders the toon result through the `ForwardOnly` path
- adds a `DepthOnly` pass for Opaque, Cutout, and TransparentWithZWrite materials so HDRP sky/fog and later geometry can see LiveToon's depth before the toon color pass; normal Transparent materials are discarded from this pass
- enables `TransparentDepthPrepass` and `TransparentDepthPostpass` for Transparent and TransparentWithZWrite materials so HDRP can stabilize transparent ordering without changing preserved MToon cull values
- keeps LiveToon's Forward pass on `ZTest LEqual`; the converter also writes `_ZTeForLiOpa = LEqual` for compatibility, but the shader does not depend on stale generated material values for the main depth test
- keeps the outline pass on `ZTest LEqual` and the material's `_ZWrite` state so outline behavior remains aligned with the converted material render mode
- enables HDRP transparent fog for Transparent materials while keeping the shader-side fog path guarded by `_ENABLE_FOG_ON_TRANSPARENT`
- keeps VRM Transparent materials as Transparent and uses LiveToon's legacy transparent opacity formula based on `_TransparentThreshold`, `_MainTex` alpha/red, and `_Color.a`
- keeps source outline settings by default; enable `Disable Outline After Convert` only when isolating outline rendering issues
- optionally creates legacy material backup assets beside the source material for older in-place debugging workflows
- can still restore material asset values from those legacy backups with `Restore Legacy Material Assets From Backups`

Legacy backups are stored in a `LiveToonMaterialBackups` folder next to the source material asset. The converter keeps the first backup instead of overwriting it, so you can compare original VRM 0.x material values while debugging culling and double-sided rendering.

---

## Directional Light Fallback

LiveToon uses HDRP Directional Lights as the main toon key light so scene shadows and character shadow controls stay predictable. If a scene has no Directional Light, the shader first builds a slightly boosted fallback key light from nearby HDRP punctual lights using their distance and spot attenuation. This keeps point and spot lights useful in lightless preview or stage scenes without making them replace the normal Directional Light path.

The fallback only runs when `_DirectionalLightCount` is zero. Scene Directional Lights still use normal HDRP shadow attenuation, while the fallback skips scene shadow sampling. If no punctual light reaches the pixel, LiveToon falls back to a camera-facing key light. Lightless scenes also get a small final-color base lift from punctual diffuse light so the character does not stay capped at the darker toon band. Tune `_FallbackLightIntensity` and `_FallbackLightColor` on the material when a lightless preview or stage setup needs a different default brightness or tint.

## Punctual Light Intensity

Point, spot, and other HDRP punctual lights are added as a secondary rim-like accent instead of replacing the Directional Light key. LiveToon keeps the Directional Light path unchanged and scales punctual lights down before applying them, so local fixtures do not blow out VRM materials by default.

Tune `_PunctualLightIntensity` on the material when a scene needs stronger or weaker local-light response. Start near `1` for normal stage fixtures and lower it when point or spot lights are used close to the character.

## MToon Rim

LiveToon keeps the stage-style custom rim separate from the original MToon rim controls. The MToon rim path uses `_RimColor`, `_RimTexture`, `_RimLightingMix`, `_RimFresnelPower`, and `_RimLift`, then adds that result to the final color once.

Use `_CustomRimIntensity` to balance LiveToon's custom local-light rim against the original MToon rim. The custom rim uses bounded punctual diffuse light instead of the full punctual shaded color, so nearby point and spot lights should act as a visible accent rather than a second over-bright lighting pass. When no Directional Light exists, the bound is relaxed a little so local fixtures still read as a rim. Materials with black `_RimColor` remain unchanged for the MToon rim path.

---

## LiveToon Legacy Snapshot

`Assets/toshi.VLiveKit/livetoon_legacy` keeps a side-by-side snapshot of `Assets/toshi.VLiveKit/livetoon` from commit `411131ffd64863e66531960cf47656823bd3b932`.

Use it as a reference for the older LoadModel-style VRM 0.x / MToon conversion behavior. Unity GUIDs, shader paths, package name, asmdef names, and the legacy converter menu path are renamed so the snapshot can live next to the current LiveToon sources without colliding.
