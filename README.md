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
- キャラルートから制御する顔法線、顔向けライト方向制限、接地を崩しにくい視野角補正
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

The converter has a `Conversion Source` selector. `MToon` keeps the non-destructive LoadModel-style baseline for VRM 0.x / MToon materials:

- use the Editor Window for one-off conversion, or add `LiveToonShaderConverter` to a scene object to save conversion settings per scene
- the `LiveToonShaderConverter` component exposes the same convert and restore buttons in its Inspector
- creates fresh LiveToon material copies on every conversion run and assigns those copies to the selected model renderers
- keeps the original source material assets unchanged
- stores copies in a `LiveToonMaterials` folder next to source materials under `Assets/`
- stores copies under `Assets/VLiveKitGenerated/LiveToonMaterials` when the source material is in a package or is not an asset
- reuses and updates an existing converted copy at the same destination path instead of creating `_LiveToon 1`, `_LiveToon 2`, and other duplicate generations during repeated conversion tests
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

`MMD4Mecanim` conversion uses a separate material mapping so the MToon path stays unchanged:

- copies `_Color`, `_MainTex`, and a `_ShadeColor` approximation from the MMD material's `_Ambient`
- maps MMD edge settings from `_EdgeColor` and `_EdgeSize` into LiveToon outline properties
- forces emission off unless MMD4Mecanim has a real `_Emissive` color or `_EmissionMap`, avoiding the all-white look from default white `_EmissionColor`
- keeps MMD shader-name hints such as `Transparent`, `Edge`, and `BothFaces`, plus `_Mode`, `_RenderQueue`, material names such as `hairshadow` / `eye_hi`, and `_Color.a`; MMD transparent shader variants become Opaque when the main texture has no transparency, Cutout when it does, and Transparent when the material name marks a soft translucent layer such as `hairshadow`, `eye_hi`, `cheek`, `decal`, `lens`, `Sleeve`, `Shadow`, `HL`, `Tear`, `Brow`, `Eyelash`, `_AL`, or `+`; no-alpha soft layers lower `_Color.a` so LiveToon's RGB-based transparent mask can reproduce MMD-style sleeve fades
- uses backface culling for opaque hand materials even when the source shader says `BothFaces`, avoiding visible sleeve interiors and double-sided artifacts on closed MMD clothing meshes
- hides MMD materials whose diffuse alpha is effectively zero by raising the LiveToon cutout threshold, so disabled overlay layers do not render as solid white parts
- tracks FBX-embedded source materials with Unity `GlobalObjectId`, material name, and source shader name, so repeated conversion does not collapse every material back to the first material inside the FBX

Legacy backups are stored in a `LiveToonMaterialBackups` folder next to the source material asset. The converter keeps the first backup instead of overwriting it, so you can compare original VRM 0.x material values while debugging culling and double-sided rendering.

---

## Directional Light Fallback

LiveToon uses HDRP Directional Lights as the main toon key light so scene shadows and character shadow controls stay predictable. If a scene has no Directional Light, the shader first builds a slightly boosted fallback key light from nearby HDRP punctual lights using their distance and spot attenuation. This keeps point and spot lights useful in lightless preview or stage scenes without making them replace the normal Directional Light path.

The fallback only runs when `_DirectionalLightCount` is zero. Scene Directional Lights still use normal HDRP shadow attenuation, while the fallback skips scene shadow sampling. If no punctual light reaches the pixel, LiveToon falls back to a camera-facing key light. Lightless scenes also get a small final-color base lift from punctual diffuse light so the character does not stay capped at the darker toon band. Tune `_FallbackLightIntensity` and `_FallbackLightColor` on the material when a lightless preview or stage setup needs a different default brightness or tint.

## Punctual Light Intensity

Point, spot, and other HDRP punctual lights are added as a secondary rim-like accent instead of replacing the Directional Light key. LiveToon keeps the Directional Light path unchanged and scales punctual lights down before applying them, so local fixtures do not blow out VRM materials by default.

Tune `_PunctualLightIntensity` on the material when a scene needs stronger or weaker local-light response. Start near `1` for normal stage fixtures and lower it when point or spot lights are used close to the character.

## Environment Lighting

LiveToon uses HDRP environment data without switching back to the full Lit/GBuffer path.

`_IndirectLightIntensity` adds diffuse ambient light from HDRP's Ambient Probe, so Sky and Environment Lighting settings can lift the toon character without requiring a scene Directional Light. New LiveToon materials default to `0.35` so HDRI lighting is visible but still secondary to the toon key light.

`_ReflectionProbeIntensity` and `_ReflectionProbeSmoothness` add a controlled specular environment term from HDRP Reflection Probes, falling back to the HDRP Sky when no local probe fills the reflection hierarchy. Keep the reflection intensity low for normal VRM characters, then raise it for glossy stage outfits or scenes where the character should pick up the room.

## Material Inspector

LiveToon uses `LiveToonInspector_MToonBase` as its shader inspector. It draws the normal `MToon.MToonInspector` first, then appends a `LiveToon Options` section for LiveToon-only material controls such as reflection probes, punctual/fallback light response, hair specular, custom rim intensity, perspective controls, and material role flags.

## Hair Specular

Hair slots marked by `LiveToonCharacterLookController` through `Hair Renderers` or `Hair Materials` receive the LiveToon hair specular path. The highlight is controlled by `_SpecColor`, `_Intensity`, `_Sharpness`, `_Position`, `_JitterIntensity`, and `_JitterTex` in `LiveToon Options`.

The shader converter assigns the package default `Shader/jitter.png` to `_JitterTex` on converted LiveToon materials when the slot is empty. Existing materials can be fixed from the material inspector with `Assign Default Jitter Texture`.

The hair specular is masked by surface direction and the scene Directional Light shadow attenuation, so it fades out on the shadowed side while the main toon hair shading can still keep its softer LiveToon look.

## Wet Skin

LiveToon material options include a lightweight wet-skin layer for rain or sweat shots.

`_SweatIntensity` enables procedural UV-space droplets and short trails on the material. Use it on face/body materials for sweat or rain beads without changing the mesh. `_SweatScale`, `_SweatSpeed`, `_SweatHighlight`, `_SweatTrail`, and `_SweatColor` tune the density, animation, and shine.

`_WetHairOverlayTex` blends a manually authored stuck-hair texture onto the same material UVs. Use a transparent texture where alpha marks thin wet hair strands; `_WetHairOverlayColor`, `_WetHairOverlayIntensity`, and `_WetHairOverlayGloss` control the pasted hair color and wet highlight. This material overlay is preferred over HDRP Decal Projectors for LiveToon because the toon shader owns the ForwardOnly color path.

Prompt for generating a wet-hair overlay texture:

```text
transparent PNG texture, alpha background, thin wet anime hair strands stuck to skin, several tapered clumps and loose strands, black to dark brown ink-like hair, glossy wet edges, no face, no eyes, no skin, no shadows, no background, clean UV decal texture, high resolution, centered composition, suitable for overlaying on an anime character face or neck material
```

## MToon Rim

LiveToon keeps the stage-style custom rim separate from the original MToon rim controls. The MToon rim path uses `_RimColor`, `_RimTexture`, `_RimLightingMix`, `_RimFresnelPower`, and `_RimLift`, then adds that result to the final color once.

Use `_CustomRimIntensity` to balance LiveToon's custom local-light rim against the original MToon rim. The custom rim uses bounded punctual diffuse light instead of the full punctual shaded color, so nearby point and spot lights should act as a visible accent rather than a second over-bright lighting pass. When no Directional Light exists, the bound is relaxed a little so local fixtures still read as a rim. Materials with black `_RimColor` remain unchanged for the MToon rim path.

---

## Character Look Controller

For the normal character-root workflow, add `LiveToonSetup` to the character root first.

`LiveToonSetup` checks for a Humanoid `Animator` under the root. When the model is Humanoid, it automatically adds and wires:

- `LiveToonCharacterLookController` for spherical face normals, face-space Directional Light limiting, and perspective correction.
- `LiveToonFrontHairShadowLight` for the crisp custom front-hair shadow depth light synced to the scene Directional Light.
- `LiveToonBoxShadowLight` on a head-child GameObject named `VLiveBoxLight`, using the front-hair-to-face debug defaults while leaving caster and receiver renderers manual.

The setup uses the Humanoid head bone for the face reference, front-hair shadow center, and the head-following box shadow light. The box shadow light copies only the rotation from `Source Directional Light`, so its position follows the head while its projection direction stays aligned with the specified scene light. The character look controller and front-hair shadow light keep their normal automatic renderer collection, while the box shadow light expects explicit `Shadow Casters` and `Shadow Receivers` assignments so you can isolate the front hair and face meshes. Use the component context menu `Setup LiveToon` after changing the Animator or source Directional Light manually.

`LiveToonSetup` also exposes `Override Shadow Boundary Saturation` for character-wide control of the `_Sat` shadow-boundary saturation boost. Use `0` to disable the boundary boost, `1` for the normal LiveToon boost, and values above `1` for a stronger effect. The value is written through `MaterialPropertyBlock` per renderer material slot, so source material assets stay unchanged; turning the override off reapplies each material's own `_Sat` value.

Add `LiveToonCharacterLookController` to the character root to drive the character-level look helpers from the scene instead of editing every material by hand.

The component writes values through `MaterialPropertyBlock` per renderer material slot, so source material assets stay unchanged. It automatically targets materials using `toshi/VLiveKit/livetoon`, keeps face slots manual by default, and can still auto-detect hair slots from renderer/material names. Use the explicit Face renderer/material lists for face parts that should receive spherical normals and face-space light limiting.

In Edit Mode, the controller updates only when its relevant transforms/settings change and is throttled by `Edit Mode Update Interval`. Play Mode still updates every frame so animated head/root motion stays responsive.

Controls:

- Spherical Normals: blends face material normals toward a sphere centered around the head. This stabilizes face lighting without changing the mesh.
- Directional Light Limit: softly holds the Directional Light near preferred face-space yaw angles, then eases through the in-between angles so face shadows avoid hard pops while still moving past less flattering directions quickly. `Face Light Yaw Sticky Range` controls how strongly the light stays on those preferred angles.
- Perspective Correction: reduces perspective distortion in the vertex pass while fading from the root/ground height, so the feet and contact area are not pulled around as strongly.

Perspective correction is applied to the forward, outline, depth-only, transparent depth, and shadow caster vertex paths. Keeping those paths aligned is important in HDRP; if only the color pass moves, depth and transparent composition can make body parts appear cut out.

When the component is selected, `Show Spherical Normal Gizmo` draws the spherical-normal center from `Head` plus `Head Position Offset`, with a wire sphere and short head-axis guide lines. `Spherical Normal Gizmo Radius` is visual only; the shader uses the center point and vertex positions to build the spherical normal direction.

Recommended setup:

1. Add `LiveToonCharacterLookController` to the character root.
2. Assign `Head` if the Humanoid head bone is not detected automatically.
3. Keep `Auto Collect Renderers` enabled for normal VRM-style characters.
4. Add the face mesh renderers or face materials to the Face override lists manually.
5. Leave `Auto Detect Hair Materials` enabled for normal hair naming, or add hair renderers/materials manually when names are custom.
6. Tune `Perspective Center Offset`, `Perspective Correction Height`, and `Perspective Correction Intensity` per character scale.

---

## Front Hair Shadow Light

Add `LiveToonFrontHairShadowLight` to the character root when you want a crisp custom shadow from the bangs/front hair onto the face.

The component references a scene Directional Light, renders selected hair renderers into a small custom depth texture, and sends that texture plus the matching light-space matrix to the LiveToon shader. The depth producer and shader sampler use the same matrix, so the shadow comparison stays consistent and does not rely on HDRP's main shadow map.

In Edit Mode, the custom depth render is throttled by `Edit Mode Update Interval` and only re-renders after relevant placement or light-direction changes. Play Mode still updates every frame.

Recommended setup:

1. Add `LiveToonFrontHairShadowLight` to the character root.
2. Assign `Source Directional Light`, or leave `Auto Find Directional Light` enabled for simple scenes.
3. Keep `Auto Collect Hair Casters` enabled when hair renderers/materials contain `hair`, `kami`, or `髪` in their names.
4. Keep `Shadow Casters` limited to front-hair/hair renderers. Do not include face or body renderers in the depth caster list, because they can overwrite the hair depth and hide the bang shadow.
5. Assign `Shadow Center` to the head bone when the automatic bounds feel too wide.
6. Use `Shadow Strength` for darkness and `Shadow Bias` only for acne/peter-panning correction.

`Texture Size` defaults to 1024 with point filtering for a crisp toon-style result. If alpha-cut hair cards need exact cutouts, add those renderers manually and test whether the mesh silhouette is enough before adding alpha sampling to the depth pass.

For first-pass debugging, `Force Visible Debug Shadow` is enabled by default. With `Debug Use Caster Silhouette` enabled, LiveToon darkens the projected caster silhouette even when the depth comparison is wrong, so you can confirm that the custom shadow map contains front hair. Use the component context menu `Collect Hair Casters` when `Shadow Casters` contains face or body renderers, then turn debug options off after confirming the shader receives the custom light.

---

## Box Shadow Light

`LiveToonBoxShadowLight` is a simpler test light for isolating custom front-hair shadows. Add it to an empty GameObject, place the box around the front hair and face, and orient the transform's forward axis from the caster side toward the receiver side.

When using `LiveToonSetup`, the component creates or reuses a head-child `VLiveBoxLight`, assigns the setup's `Source Directional Light`, applies the tested front-hair-to-face defaults once, and keeps `Shadow Casters` / `Shadow Receivers` untouched for manual assignment. Put only the bang/front-hair meshes in casters and the face mesh in receivers for the cleanest first pass. To restore the tested defaults later, use the component context menu `Use Front Hair Face Shadow Defaults`.

`Source Directional Light` is direction-only for the box light. With `Sync Direction From Source Light` enabled, the component copies the source light rotation before rendering, but keeps its own head-following position, box size, shadow strength, and receiver-only material property block output.

In Edit Mode, the 4096 box shadow render is throttled by `Edit Mode Update Interval` and only runs when settings, placement, or source-light direction need an update. Play Mode still updates every frame.

Setup:

1. Add `LiveToonBoxShadowLight` to a GameObject.
2. Put the front-hair renderers in `Shadow Casters`.
3. Put the face renderer that should receive the shadow in `Shadow Receivers`.
4. Keep `Use Depth Comparison` off first. This projects the caster silhouette onto the receiver so you can verify the map, matrix, and receiver material path.
5. If the projected silhouette is mirrored, toggle `Flip U` or `Flip V`. `Flip V` defaults on because render texture projection is commonly vertically flipped on D3D.
6. If the shadowed and unshadowed areas are swapped, toggle `Invert Silhouette`.
7. After the silhouette appears, enable `Use Depth Comparison` and tune `Shadow Bias`, `Box Size`, and the transform direction.

For broader checks, assign the character root to `Target Root`, then enable `Collect Casters From Target Root` or `Collect Receivers From Target Root` separately. Enabling both while `Use Depth Comparison` is off projects the whole character silhouette onto every receiver, so use that only when you intentionally want a "shadow no matter what" stress test. Remove problem meshes with `Excluded Renderers`, `Excluded Casters`, or `Excluded Receivers`.

For a single high-resolution full-body self shadow, enable `Full Body Self Shadow Mode` or use the component context menu `Use Full Body Self Shadow Defaults`. This collects the target root as both caster and receiver, forces depth comparison, auto-fits the box to the resolved renderers, and raises the shadow texture to 4096. Rotate the component transform to choose the custom light direction, then tune `Shadow Bias` if self-shadow acne or peter-panning appears.

The component writes only to the resolved receiver renderers through `MaterialPropertyBlock`, so it is useful for testing one face mesh or the whole character without changing source materials.

---

## LiveToon Legacy Snapshot

`Assets/toshi.VLiveKit/livetoon_legacy` keeps a side-by-side snapshot of `Assets/toshi.VLiveKit/livetoon` from commit `411131ffd64863e66531960cf47656823bd3b932`.

Use it as a reference for the older LoadModel-style VRM 0.x / MToon conversion behavior. Unity GUIDs, shader paths, package name, asmdef names, and the legacy converter menu path are renamed so the snapshot can live next to the current LiveToon sources without colliding.
