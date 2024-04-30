# ReportAssetBundleSize
選択したゲームオブジェクトを[AssetBundle](https://docs.unity3d.com/ja/2022.3/Manual/AssetBundlesIntro.html)にビルドし、圧縮状態と非圧縮状態のサイズを比較します。

## 既知のバグ
* ビルド時に`IPreprocessCallbackBehaviour`が走りません ([GitHub](https://github.com/KisaragiEffective/ReportAssetBundleSize/issues/2))。
* VPMのパッケージとしてインストールできません ([GitHub](https://github.com/KisaragiEffective/ReportAssetBundleSize/issues/7))。

## インストール方法
* Gitがインストールされている場合、UPMとしてインストールします。
  1. 画面上のツールバーから`Window > PackageManager`を開きます。
  2. \[+\]から「Add package from Git URL」を選択します。
  3. URLに`https://github.com/KisaragiEffective/ReportAssetBundleSize.git` を指定します。

## ライセンス
MIT License

全文は同梱の`LICNSE`、あるいは https://opensource.org/license/mit をご覧ください。
