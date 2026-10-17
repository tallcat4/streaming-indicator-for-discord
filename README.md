# Discord Streaming Indicator

Discord で画面共有（配信）していることを忘れて個人情報を映してしまう事故を防ぐため、
配信中は全モニターの外周に赤枠を表示する Windows 常駐アプリ。

Discord クライアントには一切手を加えず、Windows が記録する情報だけを読む。

## 表示

| 条件 | 表示 |
|---|---|
| Discord が画面をキャプチャ中、または GPU で動画エンコード中 | 赤枠「● Discord 配信中」 |
| Discord がマイク使用中（通話中） | 黄枠「Discord 通話中 — 画面共有していないか確認」 |
| 状態を検出できない | 黄枠「Discord の配信状態を検出できません」 |

- 枠はクリックを透過し、配信の映像には映らない（`WDA_EXCLUDEFROMCAPTURE`）。
- ラベルはマウスカーソルが近づくとフェードアウトする（下のブラウザのタブなどを見るため）。枠は残る。
- 黄枠、ラベル文字の表示はそれぞれトレイメニューから無効にできる。

## 判定の仕組み

1. **画面キャプチャ（主判定）**: Windows のプライバシー機能が記録する
   `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\graphicsCaptureProgrammatic`
   で、実行中の Discord の `LastUsedTimeStop` が 0 なら使用中。
2. **GPU 動画エンコード（保険）**: パフォーマンスカウンター `GPU Engine` の Discord プロセスの
   `VideoEncode` 使用率が 0 より大きい。
3. **通話中**: 同じく `ConsentStore\microphone` で Discord がマイク使用中。

## 前提

**Discord のクリップ機能（設定 > クリップ）をオフにしておくこと。**
オンだとクリップ機能が常時画面をキャプチャするため、赤枠が出っぱなしになる。

## ビルド

.NET 9 SDK が必要。

```
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

`publish\DiscordStreamingIndicator.exe` を起動するとタスクトレイに常駐する。
トレイメニューの「Windows 起動時に実行」で自動起動を設定できる。
