# Streaming Indicator for Discord

Discord で画面共有（配信）していることを忘れて個人情報を映してしまう事故を防ぐため、
配信中は全モニターの外周に赤枠を表示する Windows 常駐アプリ。

Discord クライアントには一切手を加えず、Windows が記録する情報だけを読む。

> Discord 公式のツールではありません（個人が作成した非公式ツールです）。
> Discord は Discord Inc. の商標です。

## 表示

| 条件 | 表示 |
|---|---|
| Discord が画面をキャプチャ中、または GPU で動画エンコード中 | 赤枠「● Discord 画面共有 検出」 |
| Discord がマイク使用中（通話中） | 黄枠「Discord 通話 検出」 |
| 状態を検出できない | 黄枠「Discord 状態 検出不能」 |

- 枠はクリックを透過し、配信の映像には映らない（`WDA_EXCLUDEFROMCAPTURE`）。
- ラベルはマウスカーソルが近づくとフェードアウトする（下のブラウザのタブなどを見るため）。枠は残る。
- 黄枠、ラベル文字の表示はそれぞれトレイメニューから無効にできる。
- トレイメニューの「画面共有中の点滅」で、赤枠とラベルを点滅させられる。周期はゆっくり (2 秒) / 普通 (1 秒) / 速い (0.5 秒) から選べる（既定はなし）。
- 表示言語は日本語と英語。既定（自動）では Windows の表示言語か地域の書式が日本語なら日本語、それ以外は英語。トレイメニューの「言語 (Language)」で変更できる。

## 動作環境

- Windows 10 2004 以降 / Windows 11
- [.NET 10 デスクトップ ランタイム](https://dotnet.microsoft.com/download/dotnet/10.0)
  （未インストールの状態で起動すると、Windows がインストールを案内するダイアログを表示する）

## 使い方

1. **Discord のクリップ機能（設定 > クリップ）をオフにする。**
   オンだとクリップ機能が常時画面をキャプチャするため、赤枠が出っぱなしになる。
2. `StreamingIndicatorForDiscord.exe` を起動する。タスクトレイにモニターの形のアイコンが常駐する。
3. 必要ならトレイアイコンの右クリックメニューで「Windows 起動時に実行」をオンにする。

コード署名をしていないため、初回起動時に「Windows によって PC が保護されました」と表示されることがある。
その場合は「詳細情報」→「実行」で起動できる。

## 判定の仕組み

1. **画面キャプチャ（主判定）**: Windows のプライバシー機能が記録する
   `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\graphicsCaptureProgrammatic`
   で、実行中の Discord の `LastUsedTimeStop` が 0 なら使用中。
2. **GPU 動画エンコード（保険）**: パフォーマンスカウンター `GPU Engine` の Discord プロセスの
   `VideoEncode` 使用率が 0 より大きい。
3. **通話中**: 同じく `ConsentStore\microphone` で Discord がマイク使用中。

## ビルド

.NET 10 SDK が必要。

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

`publish\StreamingIndicatorForDiscord.exe` が出力される。

## ソースコード

https://github.com/tallcat4/streaming-indicator-for-discord

配布しているバイナリに対応するソースコードは、同じバージョン名のタグ（例: `v1.0.0`）から取得できる。

## ライセンス

[GNU General Public License v3.0](LICENSE)
