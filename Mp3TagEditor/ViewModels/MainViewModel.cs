using Mp3TagEditor.Models;
using Mp3TagEditor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Mp3TagEditor.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel クラス。
///
/// MVVMパターンの中心となるクラスで、アプリケーションのすべてのビジネスロジックと
/// UI状態を管理する。INotifyPropertyChanged を実装し、プロパティの変更を
/// WPFのデータバインディングエンジンに通知する。
///
/// 主な責務：
/// - MP3ファイル/フォルダの読み込み（ダイアログ・ドラッグ＆ドロップ）
/// - ファイル一覧（ObservableCollection&lt;Mp3FileInfo&gt;）の管理
/// - iTunes Search APIを使った自動タグ情報取得
/// - 手動検索と検索結果の適用
/// - タグ情報の保存（全件/選択中）
/// - カバー画像の設定・削除
/// - 進捗表示とキャンセル処理
///
/// コマンド一覧：
/// - OpenFilesCommand     : ファイル選択ダイアログを開く
/// - OpenFolderCommand    : フォルダ選択ダイアログを開く
/// - SaveAllCommand       : 変更されたすべてのファイルを保存
/// - SaveSelectedCommand  : 選択中のファイルのみ保存
/// - AutoFetchSelectedCommand : 選択中ファイルの情報をiTunesから自動取得
/// - AutoFetchAllCommand  : 全ファイルの情報を一括自動取得
/// - SearchCommand        : 手動検索クエリで検索を実行
/// - ApplySearchResultCommand : 選択した検索結果をタグに適用
/// - SetCoverImageCommand : 画像ファイルをカバー画像として設定
/// - RemoveCoverImageCommand  : カバー画像を削除
/// - SelectAllCommand     : 全ファイルを選択状態にする
/// - DeselectAllCommand   : 全ファイルの選択を解除する
/// - CancelCommand        : 実行中の非同期処理をキャンセル
/// - RemoveSelectedFilesCommand : 選択中のファイルをリストから除去
/// </summary>
public class MainViewModel : INotifyPropertyChanged
{
    /// <summary>iTunes検索サービス（API通信を担当）</summary>
    private readonly ITunesSearchService _searchService;

    /// <summary>
    /// 非同期処理のキャンセルトークンソース。
    /// AutoFetch / Search / ApplySearchResult などの非同期処理中にのみ有効な値を持つ。
    /// キャンセルボタン押下時に Cancel() を呼び出すことで処理を中断する。
    /// 処理完了後は Dispose してから null にリセットする。
    /// </summary>
    private CancellationTokenSource? _cts;

    // --- バッキングフィールド（プロパティ変更通知のために必要） ---
    private Mp3FileInfo? _selectedFile;
    private string _statusMessage = "MP3ファイルまたはフォルダをドラッグ&ドロップしてください";
    private bool _isBusy;
    private string _progressText = string.Empty;
    private int _progressValue;
    private int _progressMax = 100;
    private ObservableCollection<ITunesTrack> _searchResults = [];
    private ITunesTrack? _selectedSearchResult;

    /// <summary>
    /// コンストラクタ。サービスとすべてのコマンドを初期化する。
    ///
    /// AsyncRelayCommand には CanExecute デリゲートを渡すことで、
    /// 各ボタンの有効/無効状態を自動制御する：
    /// - SaveAllCommand    : 1件以上の変更済みファイルがある場合のみ有効
    /// - SaveSelectedCommand : 選択中ファイルに変更がある場合のみ有効
    /// - AutoFetchSelectedCommand : ファイル選択済みかつ処理中でない場合のみ有効
    /// - AutoFetchAllCommand : ファイルが1件以上かつ処理中でない場合のみ有効
    /// - SearchCommand    : ファイル選択済みかつ処理中でない場合のみ有効
    /// - ApplySearchResultCommand : 検索結果とファイルが両方選択済みの場合のみ有効
    /// </summary>
    public MainViewModel()
    {
        _searchService = new ITunesSearchService();
        Files = [];

        // コマンドの初期化
        OpenFilesCommand = new AsyncRelayCommand(OpenFilesAsync);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
        SaveAllCommand = new AsyncRelayCommand(SaveAllAsync, () => Files.Any(f => f.IsModified));
        SaveSelectedCommand = new AsyncRelayCommand(SaveSelectedAsync, () => SelectedFile?.IsModified == true);
        AutoFetchSelectedCommand = new AsyncRelayCommand(AutoFetchSelectedAsync, () => SelectedFile != null && !IsBusy);
        AutoFetchAllCommand = new AsyncRelayCommand(AutoFetchAllAsync, () => Files.Count > 0 && !IsBusy);
        SearchCommand = new AsyncRelayCommand(SearchManualAsync, _ => SelectedFile != null && !IsBusy);
        ApplySearchResultCommand = new AsyncRelayCommand(ApplySearchResultAsync, () => SelectedSearchResult != null && SelectedFile != null);
        SetCoverImageCommand = new RelayCommand(SetCoverImage, () => SelectedFile != null);
        RemoveCoverImageCommand = new RelayCommand(RemoveCoverImage, () => SelectedFile?.CoverImage != null);
        SelectAllCommand = new RelayCommand(SelectAll);
        DeselectAllCommand = new RelayCommand(DeselectAll);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RemoveSelectedFilesCommand = new RelayCommand(RemoveSelectedFiles, () => Files.Any(f => f.IsSelected));
    }

    /// <summary>
    /// 読み込まれたMP3ファイルの一覧。
    /// ObservableCollection を使用することで、要素の追加・削除が
    /// 自動的にUIのDataGridに反映される。
    /// </summary>
    public ObservableCollection<Mp3FileInfo> Files { get; }

    /// <summary>
    /// DataGrid で現在選択されているMP3ファイル。
    /// 右側の編集パネルのバインディングソースとなる。
    /// 変更時に HasSelectedFile の変更通知も合わせて発火する。
    /// </summary>
    public Mp3FileInfo? SelectedFile
    {
        get => _selectedFile;
        set
        {
            SetField(ref _selectedFile, value);
            // HasSelectedFile は SelectedFile から算出されるため、連動して通知が必要
            OnPropertyChanged(nameof(HasSelectedFile));
        }
    }

    /// <summary>
    /// ファイルが選択されているかどうかを示す算出プロパティ。
    /// 右側の編集パネルの Visibility バインディングで使用される。
    /// </summary>
    public bool HasSelectedFile => SelectedFile != null;

    /// <summary>
    /// ウィンドウ下部のステータスバーに表示するメッセージ。
    /// 操作の結果（成功/失敗）やエラー内容を一行で伝える。
    /// 例: "3 個のMP3ファイルを読み込みました", "保存エラー: アクセスが拒否されました"
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    /// <summary>
    /// 処理中（非同期操作実行中）かどうかを示すフラグ。
    /// trueの間、プログレスバーが表示され、キャンセルボタンが有効になる。
    /// また各コマンドの CanExecute が再評価され、不適切なボタンが無効化される。
    /// </summary>
    public bool IsBusy
    {
        get => _isBusy;
        set => SetField(ref _isBusy, value);
    }

    /// <summary>
    /// プログレスバーの横に表示するテキスト。
    /// 現在処理中のファイル名や操作内容を示す。
    /// 例: "読み込み中: 夜に駆ける.mp3", "検索中 (3/10): Pretender.mp3"
    /// 処理完了後は空文字列にリセットされる。
    /// </summary>
    public string ProgressText
    {
        get => _progressText;
        set => SetField(ref _progressText, value);
    }

    /// <summary>
    /// プログレスバーの現在値。
    /// 処理済みのファイル数に対応する。0 から ProgressMax まで増加する。
    /// </summary>
    public int ProgressValue
    {
        get => _progressValue;
        set => SetField(ref _progressValue, value);
    }

    /// <summary>
    /// プログレスバーの最大値。
    /// 処理対象のファイル総数に設定される。
    /// デフォルト値は 100（初期化時の仮の値）。
    /// </summary>
    public int ProgressMax
    {
        get => _progressMax;
        set => SetField(ref _progressMax, value);
    }

    /// <summary>
    /// 手動検索の結果リスト。
    /// SearchManualAsync が完了すると更新される。
    /// UIの ListBox にバインドされ、ユーザーが適用する結果を選択できる。
    /// 新しい検索が開始されると上書きされる。
    /// </summary>
    public ObservableCollection<ITunesTrack> SearchResults
    {
        get => _searchResults;
        set => SetField(ref _searchResults, value);
    }

    /// <summary>
    /// 手動検索の結果リストで選択されたトラック。
    /// ApplySearchResultCommand が有効になる条件の一つ。
    /// 「適用」ボタン押下時にこのトラックの情報が SelectedFile に書き込まれる。
    /// </summary>
    public ITunesTrack? SelectedSearchResult
    {
        get => _selectedSearchResult;
        set => SetField(ref _selectedSearchResult, value);
    }

    // --- コマンドプロパティ（XAML側でバインドする） ---
    /// <summary>ファイル選択ダイアログを開いてMP3を読み込むコマンド</summary>
    public ICommand OpenFilesCommand { get; }
    /// <summary>フォルダ選択ダイアログを開いてMP3を一括読み込みするコマンド</summary>
    public ICommand OpenFolderCommand { get; }
    /// <summary>変更されたすべてのファイルを保存するコマンド</summary>
    public ICommand SaveAllCommand { get; }
    /// <summary>選択中のファイルのみ保存するコマンド</summary>
    public ICommand SaveSelectedCommand { get; }
    /// <summary>選択中ファイルの情報をiTunesから自動取得するコマンド</summary>
    public ICommand AutoFetchSelectedCommand { get; }
    /// <summary>全ファイルの情報をiTunesから一括自動取得するコマンド</summary>
    public ICommand AutoFetchAllCommand { get; }
    /// <summary>手動検索ボックスのクエリで検索を実行するコマンド</summary>
    public ICommand SearchCommand { get; }
    /// <summary>選択した検索結果を選択中ファイルのタグに適用するコマンド</summary>
    public ICommand ApplySearchResultCommand { get; }
    /// <summary>画像ファイル選択ダイアログでカバー画像を設定するコマンド</summary>
    public ICommand SetCoverImageCommand { get; }
    /// <summary>カバー画像を削除するコマンド</summary>
    public ICommand RemoveCoverImageCommand { get; }
    /// <summary>全ファイルのチェックボックスを選択状態にするコマンド</summary>
    public ICommand SelectAllCommand { get; }
    /// <summary>全ファイルのチェックボックスの選択を解除するコマンド</summary>
    public ICommand DeselectAllCommand { get; }
    /// <summary>実行中の非同期処理をキャンセルするコマンド（処理中のみ有効）</summary>
    public ICommand CancelCommand { get; }
    /// <summary>チェックボックスで選択されたファイルをリストから除去するコマンド</summary>
    public ICommand RemoveSelectedFilesCommand { get; }

    /// <summary>
    /// ファイル選択ダイアログを表示し、選択されたMP3ファイルを読み込む。
    ///
    /// フィルター設定により .mp3 ファイルのみ表示される。
    /// Multiselect=true で複数ファイルの同時選択が可能。
    /// キャンセルした場合（ShowDialog() が false）は何もしない。
    /// </summary>
    private async Task OpenFilesAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "MP3ファイル (*.mp3)|*.mp3",
            Multiselect = true,
            Title = "MP3ファイルを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadFilesAsync(dialog.FileNames);
        }
    }

    /// <summary>
    /// フォルダ選択ダイアログを表示し、フォルダ内のMP3ファイルを読み込む。
    ///
    /// TagService.GetMp3Files でサブフォルダを含む全MP3ファイルを取得する。
    /// MP3ファイルが1件も存在しないフォルダが選択された場合は
    /// ステータスメッセージを更新して終了する。
    /// </summary>
    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "MP3ファイルが含まれるフォルダを選択"
        };

        if (dialog.ShowDialog() == true)
        {
            var files = TagService.GetMp3Files(dialog.FolderName).ToArray();
            if (files.Length == 0)
            {
                StatusMessage = "選択したフォルダにMP3ファイルが見つかりません";
                return;
            }
            await LoadFilesAsync(files);
        }
    }

    /// <summary>
    /// 指定されたファイルパスのリストからMP3ファイルを読み込み、Files コレクションに追加する。
    ///
    /// 処理の詳細：
    /// - .mp3 以外の拡張子のパスはフィルタリングして除外する
    /// - 既に読み込み済みのファイル（FilePath が一致）はスキップして重複を防ぐ
    /// - TagService.ReadTags はバックグラウンドスレッド（Task.Run）で実行する
    ///   （UIスレッドをブロックしないため）
    /// - Files コレクションへの追加は Dispatcher.Invoke で UIスレッドに戻して行う
    ///   （ObservableCollection はUIスレッド以外からの変更が禁止されているため）
    /// - 読み込み中はプログレスバーを更新してファイル名を表示する
    ///
    /// public にしているのは MainWindow.xaml.cs の HandleDropAsync からも呼び出すため。
    /// </summary>
    /// <param name="filePaths">読み込むファイルパスのコレクション</param>
    public async Task LoadFilesAsync(IEnumerable<string> filePaths)
    {
        IsBusy = true;
        // .mp3 ファイルのみにフィルタリング（大文字小文字を区別しない）
        var paths = filePaths.Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)).ToArray();
        ProgressMax = paths.Length;
        ProgressValue = 0;

        try
        {
            foreach (var path in paths)
            {
                // 既に読み込み済みのファイルはスキップ（FilePath の大文字小文字を無視して比較）
                if (Files.Any(f => f.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase)))
                {
                    ProgressValue++;
                    continue;
                }

                ProgressText = $"読み込み中: {Path.GetFileName(path)}";
                // ファイルI/OはバックグラウンドスレッドでUIをブロックしないように実行
                await Task.Run(() =>
                {
                    var info = TagService.ReadTags(path);
                    // ObservableCollection の変更はUIスレッドで行う必要がある
                    Application.Current.Dispatcher.Invoke(() => Files.Add(info));
                });
                ProgressValue++;
            }

            StatusMessage = $"{Files.Count} 個のMP3ファイルを読み込みました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"読み込みエラー: {ex.Message}";
        }
        finally
        {
            // 成功・失敗にかかわらず必ずビジー状態を解除する
            IsBusy = false;
            ProgressText = string.Empty;
        }
    }

    /// <summary>
    /// 変更済みフラグ（IsModified=true）のすべてのファイルのタグを保存する。
    ///
    /// 変更のないファイルは保存処理をスキップするため、不要なディスクI/Oを避けられる。
    /// 保存はバックグラウンドスレッドで実行し、UIスレッドをブロックしない。
    /// エラーが発生しても処理を継続し、保存済み件数をステータスに表示する。
    /// </summary>
    private async Task SaveAllAsync()
    {
        IsBusy = true;
        // 変更済みファイルのみを対象にする（ループ前にスナップショットを作成）
        var modifiedFiles = Files.Where(f => f.IsModified).ToArray();
        ProgressMax = modifiedFiles.Length;
        ProgressValue = 0;
        var savedCount = 0;

        try
        {
            foreach (var file in modifiedFiles)
            {
                ProgressText = $"保存中: {file.FileName}";
                await Task.Run(() => TagService.WriteTags(file));
                savedCount++;
                ProgressValue++;
            }

            StatusMessage = $"{savedCount} 個のファイルを保存しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}（{savedCount}個保存済み）";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
        }
    }

    /// <summary>
    /// 現在選択中のファイルのみタグを保存する。
    /// SelectedFile が null の場合は何もしない。
    /// </summary>
    private async Task SaveSelectedAsync()
    {
        if (SelectedFile == null) return;
        IsBusy = true;
        try
        {
            ProgressText = $"保存中: {SelectedFile.FileName}";
            await Task.Run(() => TagService.WriteTags(SelectedFile));
            StatusMessage = $"{SelectedFile.FileName} を保存しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存エラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
        }
    }

    /// <summary>
    /// 選択中のファイル1件のタグ情報を iTunes Search API から自動取得して適用する。
    ///
    /// CancellationTokenSource を生成して非同期処理に渡すことで、
    /// キャンセルボタン押下時に処理を安全に中断できるようにしている。
    /// </summary>
    private async Task AutoFetchSelectedAsync()
    {
        if (SelectedFile == null) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            ProgressText = $"検索中: {SelectedFile.FileName}";
            await FetchAndApplyAsync(SelectedFile, _cts.Token);
            StatusMessage = "情報を取得しました";
        }
        catch (OperationCanceledException)
        {
            // キャンセルボタン押下時は OperationCanceledException が発生する
            StatusMessage = "取得をキャンセルしました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// Files コレクション内のすべてのファイルのタグ情報を iTunes から一括取得する。
    ///
    /// 処理の詳細：
    /// - 各ファイルの処理前にキャンセルチェック（ThrowIfCancellationRequested）を行う
    /// - 1ファイルの取得が失敗しても次のファイルの処理を続ける（FetchAndApplyAsync の戻り値で判定）
    /// - API レート制限回避のため、各リクエスト間に 500ms の待機を挟む
    ///   （待機中もキャンセルに対応するため Task.Delay に CancellationToken を渡す）
    /// </summary>
    private async Task AutoFetchAllAsync()
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressMax = Files.Count;
        ProgressValue = 0;
        var fetchedCount = 0;

        try
        {
            foreach (var file in Files)
            {
                // ループ先頭でキャンセル確認（キャンセルされていれば OperationCanceledException をスロー）
                _cts.Token.ThrowIfCancellationRequested();
                ProgressText = $"検索中 ({ProgressValue + 1}/{Files.Count}): {file.FileName}";
                var success = await FetchAndApplyAsync(file, _cts.Token);
                if (success) fetchedCount++;
                ProgressValue++;

                // iTunesのAPI制限を避けるため500msの待機（キャンセル対応）
                await Task.Delay(500, _cts.Token);
            }

            StatusMessage = $"{fetchedCount}/{Files.Count} 個のファイルの情報を取得しました";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = $"取得をキャンセルしました（{fetchedCount}個取得済み）";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得エラー: {ex.Message}（{fetchedCount}個取得済み）";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 1ファイルの情報を iTunes Search API から取得して適用する内部メソッド。
    ///
    /// 検索戦略：
    /// 1. ファイルにアーティスト名または曲名が設定されている場合、それを使って検索する
    ///    （SearchByArtistAndTitleAsync: JP優先 → US フォールバック、アーティスト名あり → なしの2段階）
    /// 2. タグ情報が全くない場合は、ファイル名をクリーンアップして検索する
    ///    （SearchByFileNameAsync: トラック番号・括弧内情報を除去してから検索）
    ///
    /// 検索結果が複数件ある場合、FindBestMatch でスコアリングして最適な結果を選択する。
    /// </summary>
    /// <param name="file">タグ情報を取得・適用するMP3ファイル情報</param>
    /// <param name="ct">キャンセルトークン</param>
    /// <returns>取得・適用に成功した場合は true、結果が0件の場合は false</returns>
    private async Task<bool> FetchAndApplyAsync(Mp3FileInfo file, CancellationToken ct)
    {
        List<ITunesTrack> results;

        // アーティスト名または曲名が設定されている場合はそれで検索
        if (!string.IsNullOrWhiteSpace(file.Artist) || !string.IsNullOrWhiteSpace(file.Title))
        {
            results = await _searchService.SearchByArtistAndTitleAsync(
                file.Artist, file.Title, ct);
        }
        else
        {
            // タグ情報が一切ない場合はファイル名から検索クエリを生成して検索
            results = await _searchService.SearchByFileNameAsync(file.FileName, ct);
        }

        if (results.Count == 0)
            return false;

        // 複数の候補から最も一致度の高いトラックを選択して適用
        var best = FindBestMatch(file, results);
        await ApplyTrackInfoAsync(file, best, ct);
        return true;
    }

    /// <summary>
    /// 検索結果のリストから、対象ファイルに最も一致するトラックを選択する。
    ///
    /// スコアリングアルゴリズム：
    /// - タイトル完全一致  : +10点
    /// - タイトル部分一致  : +5点
    /// - アーティスト完全一致 : +10点
    /// - アーティスト部分一致 : +5点
    /// - ファイル名にトラック名を含む : +3点
    /// - ファイル名にアーティスト名を含む : +3点
    ///
    /// 最高スコアのトラックを返す。結果が1件のみの場合はスコアリングを省略する。
    /// すべての比較は小文字に変換して行うため、大文字小文字の違いを無視できる。
    /// </summary>
    /// <param name="file">対象のMP3ファイル情報</param>
    /// <param name="results">iTunes検索結果リスト（1件以上）</param>
    /// <returns>最も一致度の高いITunesTrack</returns>
    private static ITunesTrack FindBestMatch(Mp3FileInfo file, List<ITunesTrack> results)
    {
        // 1件のみなら選択の余地がないのでそのまま返す
        if (results.Count == 1)
            return results[0];

        var fileName = Path.GetFileNameWithoutExtension(file.FilePath).ToLowerInvariant();
        var title = file.Title.ToLowerInvariant();
        var artist = file.Artist.ToLowerInvariant();

        // 各候補にスコアを付けて降順ソートし、最高スコアのものを返す
        var scored = results.Select(r =>
        {
            var score = 0;
            var rTitle = (r.TrackName ?? "").ToLowerInvariant();
            var rArtist = (r.ArtistName ?? "").ToLowerInvariant();

            // タイトル一致スコア
            if (!string.IsNullOrEmpty(title) && rTitle == title) score += 10;
            else if (!string.IsNullOrEmpty(title) && (rTitle.Contains(title) || title.Contains(rTitle))) score += 5;

            // アーティスト一致スコア
            if (!string.IsNullOrEmpty(artist) && rArtist == artist) score += 10;
            else if (!string.IsNullOrEmpty(artist) && (rArtist.Contains(artist) || artist.Contains(rArtist))) score += 5;

            // ファイル名との一致スコア（タグ情報がない場合に有効）
            if (fileName.Contains(rTitle) && !string.IsNullOrEmpty(rTitle)) score += 3;
            if (fileName.Contains(rArtist) && !string.IsNullOrEmpty(rArtist)) score += 3;

            return (Track: r, Score: score);
        });

        return scored.OrderByDescending(s => s.Score).First().Track;
    }

    /// <summary>
    /// ITunesTrack の情報を Mp3FileInfo に書き込む。
    ///
    /// null の場合は既存の値を保持する（null-coalescing 演算子 ?? を使用）。
    /// アルバムアーティストは以下の優先順位で設定する：
    ///   1. collectionArtistName（コンピレーションアルバム向け）
    ///   2. artistName（通常のアルバムアーティスト）
    ///   3. 既存の値（変更しない）
    ///
    /// アートワークは高解像度版（600x600）を優先してダウンロードし、
    /// 失敗した場合は 100x100 版にフォールバックする。
    /// ダウンロード失敗時は既存のカバー画像を保持する。
    /// </summary>
    /// <param name="file">適用先のMP3ファイル情報</param>
    /// <param name="track">適用するiTunesトラック情報</param>
    /// <param name="ct">キャンセルトークン</param>
    private async Task ApplyTrackInfoAsync(Mp3FileInfo file, ITunesTrack track, CancellationToken ct)
    {
        file.Title = track.TrackName ?? file.Title;
        file.Artist = track.ArtistName ?? file.Artist;
        file.Album = track.CollectionName ?? file.Album;
        // アルバムアーティスト: コレクションのアーティスト名 > トラックのアーティスト名 > 既存値
        file.AlbumArtist = track.CollectionArtistName ?? track.ArtistName ?? file.AlbumArtist;
        file.TrackNumber = (uint)track.TrackNumber;
        file.Genre = track.PrimaryGenreName ?? file.Genre;

        // 年が取得できた場合のみ上書き（0の場合は既存の値を保持）
        if (track.ReleaseYear > 0)
            file.Year = track.ReleaseYear;

        // アートワークをダウンロード（600x600高解像度版を優先し、なければ100x100）
        var artworkUrl = track.ArtworkUrl600 ?? track.ArtworkUrl100;
        var imageData = await _searchService.DownloadArtworkAsync(artworkUrl, ct);
        if (imageData != null)
        {
            file.CoverImageData = imageData;
        }
    }

    /// <summary>
    /// 検索ボックスのテキストを使って手動検索を実行する。
    ///
    /// parameter（XAMLのCommandParameter）に文字列が渡された場合はそれを検索クエリとして使う。
    /// パラメータが空の場合は、選択中ファイルのアーティスト名＋曲名を組み合わせて検索する。
    /// それも空の場合はファイル名（拡張子なし）を検索クエリとして使う。
    ///
    /// 検索結果は SearchResults コレクションに格納され、UIのリストに表示される。
    /// </summary>
    /// <param name="parameter">検索クエリ文字列（MainWindow.xaml.cs から渡される）</param>
    private async Task SearchManualAsync(object? parameter)
    {
        if (parameter is not string query || string.IsNullOrWhiteSpace(query))
        {
            // 検索クエリが指定されていない場合は選択中ファイルの情報から自動生成
            if (SelectedFile == null) return;
            query = $"{SelectedFile.Artist} {SelectedFile.Title}".Trim();
            if (string.IsNullOrWhiteSpace(query))
                query = Path.GetFileNameWithoutExtension(SelectedFile.FilePath);
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            ProgressText = $"検索中: {query}";
            var results = await _searchService.SearchAsync(query, _cts.Token);
            SearchResults = new ObservableCollection<ITunesTrack>(results);
            StatusMessage = $"{results.Count} 件の検索結果";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "検索をキャンセルしました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"検索エラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 手動検索の結果リストで選択したトラック情報を、選択中のMP3ファイルに適用する。
    ///
    /// ApplyTrackInfoAsync を呼び出してタグ情報とアートワークを設定する。
    /// 適用後は SelectedFile の IsModified が true になり、保存ボタンが有効化される。
    /// </summary>
    private async Task ApplySearchResultAsync()
    {
        if (SelectedFile == null || SelectedSearchResult == null) return;

        IsBusy = true;
        _cts = new CancellationTokenSource();
        try
        {
            ProgressText = "情報を適用中...";
            await ApplyTrackInfoAsync(SelectedFile, SelectedSearchResult, _cts.Token);
            StatusMessage = $"「{SelectedSearchResult.TrackName}」の情報を適用しました";
        }
        catch (Exception ex)
        {
            StatusMessage = $"適用エラー: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            ProgressText = string.Empty;
            _cts?.Dispose();
            _cts = null;
        }
    }

    /// <summary>
    /// 画像ファイル選択ダイアログを開き、選択した画像を選択中ファイルのカバー画像として設定する。
    ///
    /// 対応画像形式: JPEG (.jpg/.jpeg), PNG (.png), BMP (.bmp)
    /// 画像データは byte[] としてファイルから読み込み、CoverImageData プロパティに設定する。
    /// CoverImageData のセッターが BitmapImage を自動生成してUIに反映する。
    /// </summary>
    private void SetCoverImage()
    {
        if (SelectedFile == null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "画像ファイル (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
            Title = "カバー画像を選択"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // 画像ファイルをバイナリ読み込みし、そのままタグデータとして使用する
                var imageData = File.ReadAllBytes(dialog.FileName);
                SelectedFile.CoverImageData = imageData;
                StatusMessage = "カバー画像を設定しました";
            }
            catch (Exception ex)
            {
                StatusMessage = $"画像読み込みエラー: {ex.Message}";
            }
        }
    }

    /// <summary>
    /// 選択中のファイルのカバー画像を削除する。
    /// CoverImageData に null を設定することで、UIの画像表示もクリアされる。
    /// IsModified が true になるため、保存時にファイルから埋め込み画像が削除される。
    /// </summary>
    private void RemoveCoverImage()
    {
        if (SelectedFile == null) return;
        SelectedFile.CoverImageData = null;
        StatusMessage = "カバー画像を削除しました";
    }

    /// <summary>
    /// Files コレクション内のすべてのファイルの IsSelected を true に設定する。
    /// DataGrid の各行左端のチェックボックスが全てオンになる。
    /// </summary>
    private void SelectAll()
    {
        foreach (var file in Files)
            file.IsSelected = true;
    }

    /// <summary>
    /// Files コレクション内のすべてのファイルの IsSelected を false に設定する。
    /// DataGrid の各行左端のチェックボックスが全てオフになる。
    /// </summary>
    private void DeselectAll()
    {
        foreach (var file in Files)
            file.IsSelected = false;
    }

    /// <summary>
    /// 実行中の非同期処理（自動取得・検索・適用）をキャンセルする。
    /// _cts（CancellationTokenSource）の Cancel() を呼び出すことで、
    /// 各非同期メソッド内の ThrowIfCancellationRequested や Task.Delay が
    /// OperationCanceledException をスローし、処理が中断される。
    /// </summary>
    private void Cancel()
    {
        _cts?.Cancel();
    }

    /// <summary>
    /// チェックボックスで選択されたファイル（IsSelected=true）を Files コレクションから除去する。
    ///
    /// ファイルはリストから除去されるだけで、ディスク上のMP3ファイルは削除されない。
    /// ループ前に selected リストを作成（ToList）することで、
    /// コレクション変更中のイテレーション問題を回避している。
    /// </summary>
    private void RemoveSelectedFiles()
    {
        // ループ中にコレクションを変更すると例外が発生するため、先にリストとして取得
        var selected = Files.Where(f => f.IsSelected).ToList();
        foreach (var file in selected)
            Files.Remove(file);
        StatusMessage = $"{selected.Count} 個のファイルをリストから除去しました";
    }

    /// <summary>
    /// ドラッグ＆ドロップで受け取ったパスを処理する。
    ///
    /// パスがフォルダの場合は TagService.GetMp3Files で再帰的にMP3を検索する。
    /// パスが .mp3 ファイルの場合は直接リストに追加する。
    /// それ以外の拡張子のファイルは無視する。
    ///
    /// MP3ファイルが1件も見つからない場合はステータスメッセージを更新して終了する。
    ///
    /// public にしているのは MainWindow.xaml.cs の Window_Drop ハンドラーから呼ぶため。
    /// </summary>
    /// <param name="paths">ドロップされたファイル/フォルダのパス配列</param>
    public async Task HandleDropAsync(string[] paths)
    {
        var mp3Files = new List<string>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                // フォルダがドロップされた場合はサブフォルダを含む全MP3を取得
                mp3Files.AddRange(TagService.GetMp3Files(path));
            }
            else if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                // MP3ファイルが直接ドロップされた場合はそのまま追加
                mp3Files.Add(path);
            }
            // それ以外の拡張子（画像・テキスト等）は無視する
        }

        if (mp3Files.Count == 0)
        {
            StatusMessage = "MP3ファイルが見つかりません";
            return;
        }

        await LoadFilesAsync(mp3Files);
    }

    /// <summary>
    /// プロパティ変更イベント。WPFのデータバインディングエンジンが購読する。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// PropertyChanged イベントを発火するヘルパーメソッド。
    /// [CallerMemberName] により呼び出し元のプロパティ名が自動的に渡される。
    /// </summary>
    /// <param name="propertyName">変更されたプロパティ名（自動取得）</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// バッキングフィールドを更新し、値が変わった場合にのみ PropertyChanged を発火する。
    /// 値が同一なら何もせず false を返す（無限ループ防止と不要な通知の抑制）。
    /// </summary>
    /// <typeparam name="T">プロパティの型</typeparam>
    /// <param name="field">バッキングフィールドへの参照</param>
    /// <param name="value">新しい値</param>
    /// <param name="propertyName">プロパティ名（自動取得）</param>
    /// <returns>値が変更された場合は true、変更がなかった場合は false</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
