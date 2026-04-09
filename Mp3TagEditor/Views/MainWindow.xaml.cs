using Mp3TagEditor.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace Mp3TagEditor.Views;

/// <summary>
/// MainWindow.xaml のコードビハインド。
///
/// MVVMパターンに従い、このクラスはUIイベントの処理のみを担当する。
/// ビジネスロジック（ファイルの読み込み、タグ取得、保存等）はすべて
/// MainViewModel に委譲している。
///
/// このクラスが直接担当する処理：
/// - ドラッグ＆ドロップ（DragOver / Drop イベント）
/// - 検索ボックスの Enter キー押下によるコマンド実行
/// - 検索ボタンクリックによるコマンド実行
///
/// DataContext には MainViewModel インスタンスが設定されており、
/// XAML 側のバインディングを通じて ViewModel の各プロパティ・コマンドと連携する。
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// コンストラクタ。XAML 定義の UI コンポーネントを初期化する。
    /// InitializeComponent() により、XAML ファイルで定義されたコントロールが
    /// インスタンス化され、DataContext に MainViewModel が設定される。
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// DataContext から MainViewModel を取得するヘルパープロパティ。
    /// コードビハインドから ViewModel のメソッドやコマンドに安全にアクセスするために使用する。
    /// DataContext が MainViewModel でない場合は InvalidCastException が発生する。
    /// </summary>
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    /// <summary>
    /// ウィンドウ上にファイル/フォルダがドラッグされたときのイベントハンドラー。
    ///
    /// ドラッグされているデータがファイルドロップ形式（FileDrop）の場合のみ
    /// ドロップ操作を許可する（DragDropEffects.Copy）。
    /// それ以外の場合（テキスト等）はドロップを禁止する（DragDropEffects.None）。
    ///
    /// e.Handled = true を設定することで、親要素への DragOver イベントの
    /// バブリングを抑制し、意図しない動作を防ぐ。
    /// </summary>
    /// <param name="sender">イベント発生元（MainWindow）</param>
    /// <param name="e">ドラッグイベントの引数（ドラッグデータや許可操作を含む）</param>
    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // ファイルドロップ形式ならコピー操作を許可（カーソルが矢印＋コピーアイコンに変わる）
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            // ファイル以外のデータ（テキスト等）はドロップ禁止（カーソルが禁止アイコンに変わる）
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// ウィンドウ上にファイル/フォルダがドロップされたときのイベントハンドラー。
    ///
    /// ドロップされたパスの配列を ViewModel の HandleDropAsync に渡す。
    /// HandleDropAsync がパスを解析し、MP3ファイルをリストに追加する。
    ///
    /// async void を使用しているのは、WPFのイベントハンドラーが void 型を要求するため。
    /// 例外は ViewModel 内でキャッチされ、StatusMessage に表示される。
    /// </summary>
    /// <param name="sender">イベント発生元（MainWindow）</param>
    /// <param name="e">ドロップイベントの引数（ドロップされたファイルパスを含む）</param>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // ドロップされたパスの配列を取得（ファイル・フォルダが混在する場合もある）
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (paths != null)
            {
                await ViewModel.HandleDropAsync(paths);
            }
        }
    }

    /// <summary>
    /// 検索ボックスでキーが押されたときのイベントハンドラー。
    ///
    /// Enter キーが押された場合のみ検索を実行する。
    /// これにより、検索ボタンを使わなくてもキーボードのみで検索できる。
    /// </summary>
    /// <param name="sender">イベント発生元（SearchBox テキストボックス）</param>
    /// <param name="e">キーイベントの引数（押されたキーを含む）</param>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ExecuteSearch();
        }
    }

    /// <summary>
    /// 検索ボタンがクリックされたときのイベントハンドラー。
    /// ExecuteSearch() を呼び出して検索を実行する。
    /// </summary>
    /// <param name="sender">イベント発生元（SearchButton）</param>
    /// <param name="e">RoutedEventArgs（未使用）</param>
    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        ExecuteSearch();
    }

    /// <summary>
    /// 検索ボックスのテキストを取得し、ViewModel の SearchCommand を実行する。
    ///
    /// SearchBox.Text の前後の空白を除去（Trim）してからコマンドに渡す。
    /// CanExecute が false の場合（例: ファイル未選択、処理中）は実行しない。
    /// 空白のみのテキストの場合は SearchCommand 側でハンドリングされる。
    /// </summary>
    private void ExecuteSearch()
    {
        var query = SearchBox.Text?.Trim();
        if (ViewModel.SearchCommand.CanExecute(query))
        {
            ViewModel.SearchCommand.Execute(query);
        }
    }
}
