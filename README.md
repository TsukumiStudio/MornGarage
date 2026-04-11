# MornGarage

<p align="center">
  <img src="src/Editor/MornGarage.png" alt="MornGarage" width="640" />
</p>

<p align="center">
  <img src="https://img.shields.io/github/license/TsukumiStudio/MornGarage" alt="License" />
</p>

## 概要

使用頻度の低い小粒のユーティリティをまとめた物置きライブラリ。Singleton / Math / Pools / Hit2d / Physics2d / PopUps / Grids / StateMachine / SwordTrail などを 16 個の細粒度 asmdef で提供し、必要なものだけ参照できる。

## 導入方法

Unity Package Manager で以下の Git URL を追加:

```
https://github.com/TsukumiStudio/MornGarage.git?path=src#1.0.0
```

`Window > Package Manager > + > Add package from git URL...` に貼り付けてください。

## 含まれるサブライブラリ

| asmdef | 内容 |
|---|---|
| `MornGarage.Singleton` | シングルトン基底クラス (`MornSingleton<T>` / `MornSingletonMono<T>` / `MornSingletonSo<T>`) |
| `MornGarage.Math` | 数学ユーティリティ |
| `MornGarage.Frame` | FPS カウンタ・モニタ |
| `MornGarage.SerializableDictionary` | シリアライズ可能 Dictionary |
| `MornGarage.Dictionary` (+`.Editor`) | 列挙型 / オブジェクトキー Dictionary |
| `MornGarage.StateMachine` | 状態マシン + StatePattern |
| `MornGarage.SwordTrail` | 剣の軌跡エフェクト |
| `MornGarage.Debug` | デバッグログ表示 |
| `MornGarage.Graphics` | レーダーチャート等 UI Graphic |
| `MornGarage.Grids` | グリッド座標系 |
| `MornGarage.Hit2d` | 2D 当たり判定 |
| `MornGarage.Mono` | 各種 MonoBehaviour ヘルパー (BillBoard / Layout / Dropdown 等) |
| `MornGarage.Physics2d` | 2D 物理拡張 |
| `MornGarage.Pools` | オブジェクトプール (`MornObjectPool` / `MornStringBuilder` 等) |
| `MornGarage.PopUps` | ポップアップ管理 |
| `MornGarage.Types` | 型システム / 型管理 |

## ライセンス

[The Unlicense](LICENSE)
