using System.Windows.Input;

namespace Mp3TagEditor.ViewModels;

/// <summary>
/// WPFのICommandインターフェースの汎用実装クラス（同期版）。
///
/// MVVMパターンでは、UIのボタンクリック等のアクションをViewModelのメソッドに
/// バインドするためにICommandインターフェースを使用する。
/// このクラスはデリゲート（Action/Func）を受け取り、任意のメソッドを
/// コマンドとして実行できるようにする汎用的な実装。
///
/// 使用例：
///   SaveCommand = new RelayCommand(Save, () => HasChanges);
///   // ボタンのCommandプロパティにバインドすると、Save()が実行される。
///   // HasChangesがfalseの場合、ボタンは自動的に無効化される。
///
/// CanExecuteChangedイベントはCommandManager.RequerySuggestedに委譲しており、
/// WPFのUI操作（フォーカス変更、入力等）のたびに自動的にCanExecuteが再評価される。
/// </summary>
public class RelayCommand : ICommand
{
    /// <summary>コマンド実行時に呼び出されるデリゲート</summary>
    private readonly Action<object?> _execute;

    /// <summary>コマンドが実行可能かを判定するデリゲート（nullの場合は常にtrue）</summary>
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// パラメータ付きのデリゲートを受け取るコンストラクタ。
    /// CommandParameterを使用する場合に利用する。
    /// </summary>
    /// <param name="execute">コマンド実行時のアクション（引数はCommandParameter）</param>
    /// <param name="canExecute">実行可能判定（nullの場合は常にtrue）</param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// パラメータなしのデリゲートを受け取るコンストラクタ。
    /// CommandParameterが不要な場合に簡潔に記述できる。
    /// 内部的にはパラメータ付きデリゲートに変換して保持する。
    /// </summary>
    /// <param name="execute">コマンド実行時のアクション</param>
    /// <param name="canExecute">実行可能判定（nullの場合は常にtrue）</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    /// <summary>
    /// コマンドの実行可能状態が変化した可能性があるときに発火するイベント。
    /// WPFのCommandManager.RequerySuggestedに委譲することで、
    /// UI操作のたびに自動的にCanExecuteが再評価され、
    /// ボタンの有効/無効状態が自動更新される。
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// コマンドが実行可能かどうかを判定する。
    /// _canExecuteがnullの場合は常にtrueを返す。
    /// </summary>
    /// <param name="parameter">コマンドパラメータ（XAMLのCommandParameterから渡される）</param>
    /// <returns>実行可能ならtrue</returns>
    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    /// <summary>
    /// コマンドを実行する。_executeデリゲートを呼び出す。
    /// </summary>
    /// <param name="parameter">コマンドパラメータ</param>
    public void Execute(object? parameter) => _execute(parameter);
}

/// <summary>
/// WPFのICommandインターフェースの汎用実装クラス（非同期版）。
///
/// RelayCommandの非同期版で、async/awaitパターンに対応している。
/// API呼び出しやファイルI/Oなど、時間のかかる処理をUIスレッドをブロックせずに実行する。
///
/// 二重実行防止機能を備えており、コマンド実行中は_isExecutingフラグがtrueになり、
/// CanExecuteがfalseを返すため、ボタンが自動的に無効化される。
/// 実行が完了すると自動的に有効状態に戻る。
///
/// 使用例：
///   AutoFetchCommand = new AsyncRelayCommand(FetchFromITunesAsync, () => !IsBusy);
/// </summary>
public class AsyncRelayCommand : ICommand
{
    /// <summary>コマンド実行時に呼び出される非同期デリゲート</summary>
    private readonly Func<object?, Task> _execute;

    /// <summary>コマンドが実行可能かを判定するデリゲート</summary>
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// コマンドが現在実行中かどうかを示すフラグ。
    /// trueの間はCanExecuteがfalseを返し、二重実行を防止する。
    /// </summary>
    private bool _isExecuting;

    /// <summary>
    /// パラメータ付きの非同期デリゲートを受け取るコンストラクタ。
    /// </summary>
    /// <param name="execute">非同期実行アクション</param>
    /// <param name="canExecute">実行可能判定</param>
    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>
    /// パラメータなしの非同期デリゲートを受け取るコンストラクタ。
    /// </summary>
    /// <param name="execute">非同期実行アクション</param>
    /// <param name="canExecute">実行可能判定</param>
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    /// <summary>
    /// コマンドの実行可能状態が変化した可能性があるときに発火するイベント。
    /// CommandManager.RequerySuggestedに委譲する。
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// コマンドが実行可能かどうかを判定する。
    /// 実行中（_isExecuting=true）の場合は常にfalseを返し、二重実行を防止する。
    /// </summary>
    /// <param name="parameter">コマンドパラメータ</param>
    /// <returns>実行可能ならtrue</returns>
    public bool CanExecute(object? parameter) =>
        !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    /// <summary>
    /// コマンドを非同期で実行する。
    ///
    /// ICommand.Executeはvoid型のため、async voidを使用している。
    /// （async voidは通常推奨されないが、ICommandの制約上やむを得ない）
    ///
    /// 実行フロー：
    /// 1. 二重実行チェック（_isExecuting=trueなら何もしない）
    /// 2. _isExecutingをtrueに設定し、CanExecuteの再評価を要求
    /// 3. 非同期デリゲートを実行（awaitで完了を待つ）
    /// 4. finallyブロックで_isExecutingをfalseに戻し、CanExecuteの再評価を要求
    /// </summary>
    /// <param name="parameter">コマンドパラメータ</param>
    public async void Execute(object? parameter)
    {
        // 二重実行防止：既に実行中なら何もしない
        if (_isExecuting) return;

        _isExecuting = true;
        // CanExecuteの再評価を要求し、UIのボタンを無効化する
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await _execute(parameter);
        }
        finally
        {
            _isExecuting = false;
            // CanExecuteの再評価を要求し、UIのボタンを再度有効化する
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
