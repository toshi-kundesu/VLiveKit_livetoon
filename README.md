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

The converter currently uses a LoadModel-style baseline for VRM 0.x / MToon materials:

- swaps the material shader to `toshi/VLiveKit/livetoon`
- preserves existing MToon-compatible material properties such as `_CullMode`, `_BlendMode`, `_SrcBlend`, `_DstBlend`, and `_ZWrite`
- preserves `_Color`, including alpha, so MToon Transparent opacity is carried into LiveToon
- fills `_ShadeTexture` from `_MainTex` only when `_ShadeTexture` is empty
- updates `renderQueue` from `_BlendMode`: Opaque `2225`, Cutout `2450`, Transparent `3000`
- restores the material `RenderType` tag and alpha keywords from `_BlendMode` after the shader swap
- restores LiveToon's Forward pass ZTest from `_BlendMode`: Opaque uses `Equal`, Cutout/Transparent use `LEqual`
- enables HDRP transparent fog for Transparent materials while keeping the shader-side fog path guarded by `_ENABLE_FOG_ON_TRANSPARENT`
- keeps VRM Transparent materials as Transparent and uses LiveToon's legacy transparent opacity formula based on `_TransparentThreshold`, `_MainTex` alpha/red, and `_Color.a`
- disables outline by default during conversion while LiveToon outline rendering is being tuned
- optionally creates material backup assets beside the source material before converting
- restores selected model materials from those backups with `Restore Materials From Backups`

Backups are stored in a `LiveToonMaterialBackups` folder next to the source material asset. The converter keeps the first backup instead of overwriting it, so you can compare and restore the original VRM 0.x material values while debugging culling and double-sided rendering.

---

## Directional Light Fallback

LiveToon uses HDRP Directional Lights as the main toon key light so scene shadows and character shadow controls stay predictable. If a scene has no Directional Light, the shader now adds a camera-facing fallback key light instead of leaving the material black.

The fallback only runs when `_DirectionalLightCount` is zero. Scene Directional Lights still use normal HDRP shadow attenuation, while the fallback skips scene shadow sampling. Tune `_FallbackLightIntensity` and `_FallbackLightColor` on the material when a lightless preview or stage setup needs a different default brightness.

## Punctual Light Intensity

Point, spot, and other HDRP punctual lights are added as a secondary rim-like accent instead of replacing the Directional Light key. LiveToon keeps the Directional Light path unchanged and scales punctual lights down before applying them, so local fixtures do not blow out VRM materials by default.

Tune `_PunctualLightIntensity` on the material when a scene needs stronger or weaker local-light response. Start near `1` for normal stage fixtures and lower it when point or spot lights are used close to the character.

## MToon Rim

LiveToon keeps the stage-style punctual rim separate from the original MToon rim controls. The MToon rim path uses `_RimColor`, `_RimTexture`, `_RimLightingMix`, `_RimFresnelPower`, and `_RimLift`, then applies `_MToonRimIntensity` before adding it to the final color.

Use `_MToonRimIntensity` to balance the original MToon rim against LiveToon's custom local-light rim. Materials with black `_RimColor` remain unchanged.

---

## LiveToon Legacy Snapshot

`Assets/toshi.VLiveKit/livetoon_legacy` keeps a side-by-side snapshot of `Assets/toshi.VLiveKit/livetoon` from commit `411131ffd64863e66531960cf47656823bd3b932`.

Use it as a reference for the older LoadModel-style VRM 0.x / MToon conversion behavior. Unity GUIDs, shader paths, package name, asmdef names, and the legacy converter menu path are renamed so the snapshot can live next to the current LiveToon sources without colliding.
