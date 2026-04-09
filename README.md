# MP3 Tag Editor

MP3ファイルのID3タグ（楽曲情報）を編集するためのWindowsデスクトップアプリケーションです。

**バージョン:** 1.0.0

## 主な特徴

- Apple iTunes Search API を利用した楽曲情報の自動取得
- タイトル・アーティスト・アルバム・ジャンル・カバー画像などのタグ編集
- フォルダ単位での一括読み込みと一括保存
- ドラッグ＆ドロップによるファイル追加
- ダークテーマ UI

---

## ソリューション構成

```
Mp3TagEditor.sln
└── Mp3TagEditor/
    ├── Mp3TagEditor.csproj          # プロジェクト設定
    │     ターゲットフレームワーク : .NET 8.0 (Windows)
    │     出力形式               : WinExe（GUIアプリ）
    │     依存ライブラリ:
    │       - TagLibSharp 2.3.0                       (MP3タグ読み書き)
    │       - Microsoft.Xaml.Behaviors.Wpf 1.1.142    (WPF拡張動作)
    │     発行設定:
    │       - 単一ファイル出力 (PublishSingleFile=true)
    │       - 自己完結型       (SelfContained=true)
    │       - 対象プラットフォーム: win-x64
    │
    ├── App.xaml / App.xaml.cs       # アプリのエントリーポイント（MainWindow を表示）
    ├── AssemblyInfo.cs              # WPFテーマ設定
    │
    ├── Models/
    │   ├── Mp3FileInfo.cs           # MP3ファイル1件分のタグ情報モデル
    │   │     INotifyPropertyChanged を実装し、UI変更を自動反映する
    │   │     保持するフィールド: FilePath, Title, Artist, Album,
    │   │                        AlbumArtist, TrackNumber, Year, Genre,
    │   │                        CoverImage (BitmapImage), CoverImageData (byte[]),
    │   │                        IsSelected, IsModified
    │   └── ITunesSearchResult.cs    # iTunes Search API レスポンスのモデル
    │         ITunesSearchResponse : API全体のレスポンス（件数＋リスト）
    │         ITunesTrack          : 1件のトラック情報
    │
    ├── Services/
    │   ├── TagService.cs            # MP3タグの読み書きサービス（staticクラス）
    │   │     ReadTags()   : TagLibSharp でMP3からタグを読み込む
    │   │     WriteTags()  : タグ情報をMP3ファイルに書き込む
    │   │     GetMp3Files(): フォルダを再帰検索してMP3パス一覧を返す
    │   └── ITunesSearchService.cs   # iTunes Search API クライアント
    │         SearchAsync()              : クエリで検索（JP→USフォールバック）
    │         SearchByArtistAndTitleAsync(): アーティスト名＋曲名で検索
    │         SearchByFileNameAsync()    : ファイル名から検索
    │         DownloadArtworkAsync()     : アートワーク画像をダウンロード
    │         CleanFileName()            : ファイル名からノイズを除去
    │
    ├── ViewModels/
    │   ├── MainViewModel.cs         # メインウィンドウの ViewModel
    │   │     主要なプロパティ:
    │   │       Files            : 読み込まれたMP3一覧（ObservableCollection）
    │   │       SelectedFile     : 現在選択中のファイル
    │   │       StatusMessage    : ステータスバーのテキスト
    │   │       IsBusy           : 処理中フラグ
    │   │       ProgressText/Value/Max : プログレスバー制御
    │   │       SearchResults    : 手動検索結果リスト
    │   │     主要なコマンド:
    │   │       OpenFilesCommand / OpenFolderCommand
    │   │       SaveAllCommand / SaveSelectedCommand
    │   │       AutoFetchSelectedCommand / AutoFetchAllCommand
    │   │       SearchCommand / ApplySearchResultCommand
    │   │       SetCoverImageCommand / RemoveCoverImageCommand
    │   │       SelectAllCommand / DeselectAllCommand
    │   │       CancelCommand / RemoveSelectedFilesCommand
    │   └── RelayCommand.cs          # ICommand 実装クラス
    │         RelayCommand      : 同期コマンド（Action を ICommand に変換）
    │         AsyncRelayCommand : 非同期コマンド（二重実行防止機能付き）
    │
    ├── Converters/
    │   └── BoolToVisibilityConverter.cs  # WPF 値コンバーター群
    │         BoolToVisibilityConverter  : bool → Visibility
    │         NullToVisibilityConverter  : null/非null → Visibility
    │         BoolToFontWeightConverter  : bool → FontWeight (Bold/Normal)
    │
    └── Views/
        ├── MainWindow.xaml          # メインウィンドウの XAML レイアウト
        │     3段構成: ツールバー / コンテンツエリア / ステータスバー
        │     コンテンツは左右2分割:
        │       左: ファイル一覧 DataGrid（チェックボックス・各種タグ列）
        │       右: タグ編集パネル（カバー画像・各タグフィールド・手動検索）
        └── MainWindow.xaml.cs       # コードビハインド（UIイベント処理のみ）
              Window_DragOver   : ドラッグ時のカーソル制御
              Window_Drop       : ドロップされたファイルの受け取り
              SearchBox_KeyDown : Enter キーで検索実行
              SearchButton_Click: 検索ボタン押下で検索実行
```

---

## ビルド手順

### 前提条件

- Visual Studio 2022 以降（.NET デスクトップ開発 ワークロードが必要）、または .NET 8.0 SDK 以降
- インターネット接続（NuGet パッケージの復元に必要）

### 方法1: Visual Studio でビルド

1. `Mp3TagEditor.sln` をダブルクリックして Visual Studio で開く
2. 初回起動時は NuGet パッケージが自動的に復元される
3. ビルド構成を選択する
   - **Debug** : デバッグ情報付き。開発・動作確認用
   - **Release** : 最適化済み。配布・実運用用（推奨）
4. メニュー → `[ビルド]` → `[ソリューションのビルド]`、または `Ctrl+Shift+B`
5. ビルド成功後の実行ファイルの場所：
   - Debug: `Mp3TagEditor\bin\Debug\net8.0-windows\win-x64\Mp3TagEditor.exe`
   - Release: `Mp3TagEditor\bin\Release\net8.0-windows\win-x64\Mp3TagEditor.exe`

> `F5` でデバッグ実行、`Ctrl+F5` でデバッグなし実行が可能。

### 方法2: コマンドライン（dotnet CLI）

ソリューションフォルダで以下を実行：

```bash
# 通常ビルド（Debug）
dotnet build Mp3TagEditor\Mp3TagEditor.csproj

# Releaseビルド
dotnet build Mp3TagEditor\Mp3TagEditor.csproj -c Release

# 単一ファイルとして発行（配布用・推奨）
dotnet publish Mp3TagEditor\Mp3TagEditor.csproj -c Release
```

発行後のファイル: `Mp3TagEditor\bin\Release\net8.0-windows\win-x64\publish\Mp3TagEditor.exe`

> publish 版は .NET ランタイムを内包した自己完結型の単一 exe ファイルです。実行先のPCに .NET ランタイムがなくても動作します。

---

## 操作方法

### 起動

`Mp3TagEditor.exe` をダブルクリックして起動します。ダークテーマのウィンドウ（幅1280×高さ800）が中央に表示されます。最小サイズは幅900×高さ600です。

### 画面構成

```
┌──────────────────────────────────────────────────────────┐
│  ツールバー（ボタン群）                                    │
├──────────────────────────────────────┬───────────────────┤
│                                      │                   │
│  ファイル一覧（DataGrid）             │  タグ編集パネル   │
│                                      │                   │
│  ✓ | ファイル名 | タイトル | アーティ│  カバー画像       │
│  ─────────────────────────────────── │  タイトル         │
│    | 曲1.mp3   | 曲名1    | 歌手A  │  アーティスト     │
│    | 曲2.mp3   | 曲名2    | 歌手B  │  アルバム         │
│       ※変更済みは太字表示            │  トラック#・年・  │
│                                      │  ジャンル         │
│                                      │  手動検索（iTunes）│
├──────────────────────────────────────┴───────────────────┤
│  ステータスバー（メッセージ）            [プログレスバー]  │
└──────────────────────────────────────────────────────────┘
```

### MP3ファイルの読み込み

| 方法 | 操作 |
|------|------|
| ファイル指定 | ツールバーの「📂 ファイルを開く」→ MP3ファイルを選択 |
| フォルダ指定 | ツールバーの「📁 フォルダを開く」→ フォルダを選択（サブフォルダも再帰検索） |
| ドラッグ&ドロップ | エクスプローラーからMP3ファイルやフォルダをウィンドウにドロップ |

- 既に読み込み済みのファイルは重複追加されません
- MP3以外のファイルは自動的に無視されます

### タグの編集

ファイル一覧の行をクリックすると、右側パネルにタグ情報が表示されます。各フィールドを直接入力して編集できます。

| 項目 | 説明 |
|------|------|
| タイトル | 楽曲のタイトル（曲名） |
| アーティスト | 歌手・演奏者名 |
| アルバム | アルバム名 |
| アルバムアーティスト | アルバム全体のアーティスト名 |
| トラック# | アルバム内でのトラック番号 |
| 年 | リリース年（西暦4桁） |
| ジャンル | 音楽ジャンル（例: J-Pop, Rock） |

編集したファイルはファイル一覧で**太字表示**になります（未保存の目印）。

### カバー画像の操作

- **「画像を選択」**: JPEG / PNG / BMP に対応
- **「画像を削除」**: カバー画像をクリア（保存時にMP3内の埋め込み画像も削除）

> 自動取得機能では iTunes から高解像度（600×600）のアルバムアートワークを自動ダウンロードします。

### iTunesからタグを自動取得

#### 選択ファイルのみ自動取得
1. ファイル一覧でファイルをクリックして選択
2. ツールバーの「🔍 選択ファイルを自動取得」ボタンを押す

#### 全ファイルを一括自動取得
1. ツールバーの「🔍 全ファイルを一括取得」ボタンを押す
2. 「⏹ キャンセル」ボタンで途中中断も可能

#### 検索戦略
1. タグにアーティスト名または曲名がある場合 → それらで検索（フォールバックあり）
2. タグ情報が全くない場合 → ファイル名からノイズを除去して検索
3. まず日本の iTunes Store で検索 → 見つからない場合は US で再検索
4. 複数の結果がある場合 → タイトル・アーティストの一致度でスコアリングして最適な結果を選択

#### 適用される情報
タイトル / アーティスト / アルバム / アルバムアーティスト / トラック番号 / リリース年 / ジャンル / カバー画像（600×600）

### 手動検索（iTunes）

1. ファイル一覧でファイルを選択
2. 右側パネル下部のテキストボックスにキーワードを入力
3. 「検索」ボタンまたは `Enter` キーを押す
4. 検索結果（最大10件）から適用したい曲を選択
5. 「選択した結果を適用」ボタンを押す

> テキストボックスを空のまま検索すると、選択中ファイルのアーティスト名＋曲名で自動検索します。

### ファイルの保存

| ボタン | 動作 |
|--------|------|
| 「💾 すべて保存」 | 変更済み（太字）のファイルをすべてMP3に書き込む |
| 「💾 選択を保存」 | 現在選択中のファイル1件のみを書き込む |

> **注意:** 保存は元のMP3ファイルを直接上書きします。事前にバックアップを取ることを推奨します。

### ファイル一覧の操作

| ボタン | 動作 |
|--------|------|
| 「✅ 全選択」 | 全ファイルのチェックボックスをオンにする |
| 「☐ 全解除」 | 全ファイルのチェックボックスをオフにする |
| 「🗑 選択を除去」 | チェックされたファイルを一覧から除去する（ディスク上のファイルは削除されない） |

---

## 注意事項・制限

### 対応ファイル形式
- 入力: MP3 (`.mp3`) のみ
- カバー画像: JPEG (`.jpg` / `.jpeg`), PNG (`.png`), BMP (`.bmp`)

### iTunes Search API について
- インターネット接続が必要
- Apple が提供する無料の公開 API（API キー不要）
- 短時間に大量のリクエストを送ると一時的にアクセス制限される場合がある（一括取得では1件ごとに500msの待機を挟んでいます）
- API の仕様変更により、将来的に動作しなくなる可能性がある

### ファイル書き込みについて
- 保存は元のMP3ファイルを直接上書きする
- 書き込み先がネットワークドライブや読み取り専用の場合はエラーになる
- 実行中に他のアプリが同じファイルを開いていると書き込みに失敗する場合がある

### 動作環境
- OS: Windows 10 / Windows 11（64ビット）
- .NET 8.0 ランタイム（publish 版は不要・自己完結型）
- 画面解像度: 最小 1024×768 以上を推奨

---

## 使用ライブラリ

| ライブラリ | バージョン | 用途 | ライセンス |
|-----------|-----------|------|-----------|
| [TagLibSharp](https://github.com/mono/taglib-sharp) | 2.3.0 | MP3ファイルの ID3v1 / ID3v2 タグ読み書き | LGPL v2.1 |
| [Microsoft.Xaml.Behaviors.Wpf](https://github.com/microsoft/XamlBehaviorsWpf) | 1.1.142 | WPF の拡張動作（Behavior） | MIT |
| [iTunes Search API](https://developer.apple.com/library/archive/documentation/AudioVideo/Conceptual/iTuneSearchAPI/) | - | Apple 提供の楽曲情報検索 Web API（無料・キー不要） | - |
