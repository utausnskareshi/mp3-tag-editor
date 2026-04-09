using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Mp3TagEditor.Models;

/// <summary>
/// MP3ファイルのタグ情報を保持するデータモデルクラス。
///
/// WPFのデータバインディングに対応するため、INotifyPropertyChangedを実装している。
/// 各プロパティが変更されると、UIに自動的に通知され、画面表示が更新される。
///
/// タグ情報（タイトル、アーティスト、アルバム等）の編集を行うと、
/// IsModifiedフラグが自動的にtrueに設定され、未保存の変更があることを示す。
/// ファイル一覧のDataGridでは、変更済みの行が太字で表示される。
/// </summary>
public class Mp3FileInfo : INotifyPropertyChanged
{
    // --- バッキングフィールド ---
    // WPFのプロパティ変更通知を実現するため、各プロパティにバッキングフィールドを使用
    private string _filePath = string.Empty;
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string _albumArtist = string.Empty;
    private uint _trackNumber;
    private uint _year;
    private string _genre = string.Empty;
    private BitmapImage? _coverImage;
    private byte[]? _coverImageData;
    private bool _isSelected;
    private bool _isModified;

    /// <summary>
    /// MP3ファイルの絶対パス。
    /// ファイルの読み込み・保存時にこのパスを使用してディスク上のファイルにアクセスする。
    /// </summary>
    public string FilePath
    {
        get => _filePath;
        set => SetField(ref _filePath, value);
    }

    /// <summary>
    /// ファイル名のみ（ディレクトリパスを除いた部分）。
    /// UIのファイル一覧で表示するために使用する読み取り専用プロパティ。
    /// FilePathから自動的に導出される。
    /// </summary>
    public string FileName => Path.GetFileName(FilePath);

    /// <summary>
    /// 楽曲のタイトル（曲名）。
    /// ID3タグのTIT2フレームに対応する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public string Title
    {
        get => _title;
        set { SetField(ref _title, value); IsModified = true; }
    }

    /// <summary>
    /// アーティスト名（演奏者・歌手名）。
    /// ID3タグのTPE1フレームに対応する。
    /// 複数のアーティストがいる場合でも、最初の1名のみを保持する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public string Artist
    {
        get => _artist;
        set { SetField(ref _artist, value); IsModified = true; }
    }

    /// <summary>
    /// アルバム名。
    /// ID3タグのTALBフレームに対応する。
    /// シングル曲の場合はシングル名が入ることが多い。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public string Album
    {
        get => _album;
        set { SetField(ref _album, value); IsModified = true; }
    }

    /// <summary>
    /// アルバムアーティスト名。
    /// ID3タグのTPE2フレームに対応する。
    /// コンピレーションアルバムなどで、各トラックのアーティストとは別に
    /// アルバム全体のアーティストを指定する場合に使用する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public string AlbumArtist
    {
        get => _albumArtist;
        set { SetField(ref _albumArtist, value); IsModified = true; }
    }

    /// <summary>
    /// トラック番号（アルバム内での曲順）。
    /// ID3タグのTRCKフレームに対応する。
    /// 0の場合はトラック番号が未設定であることを意味する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public uint TrackNumber
    {
        get => _trackNumber;
        set { SetField(ref _trackNumber, value); IsModified = true; }
    }

    /// <summary>
    /// 楽曲の発売年（リリース年）。
    /// ID3タグのTDRCフレーム（またはTYER）に対応する。
    /// 0の場合は年が未設定であることを意味する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public uint Year
    {
        get => _year;
        set { SetField(ref _year, value); IsModified = true; }
    }

    /// <summary>
    /// 楽曲のジャンル（例: J-Pop, Rock, Anime等）。
    /// ID3タグのTCONフレームに対応する。
    /// 変更するとIsModifiedがtrueになる。
    /// </summary>
    public string Genre
    {
        get => _genre;
        set { SetField(ref _genre, value); IsModified = true; }
    }

    /// <summary>
    /// カバー画像のWPF表示用オブジェクト（BitmapImage）。
    /// CoverImageDataが設定されると、バイナリデータから自動的に生成される。
    /// UIのImageコントロールにバインドして画像を表示するために使用する。
    /// Freeze()を呼び出してスレッドセーフにしている。
    /// </summary>
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        set => SetField(ref _coverImage, value);
    }

    /// <summary>
    /// カバー画像のバイナリデータ（JPEG/PNG等の生データ）。
    /// MP3ファイルのID3タグ内APICフレームに埋め込まれる画像データ。
    ///
    /// このプロパティに値を設定すると、以下の処理が自動的に行われる：
    /// 1. IsModifiedフラグをtrueに設定
    /// 2. バイナリデータからBitmapImageを生成し、CoverImageプロパティに設定
    /// 3. nullまたは空配列が設定された場合は、CoverImageもnullにクリア
    ///
    /// BitmapImageの生成時はCacheOption.OnLoadを使用し、
    /// MemoryStreamの解放後もメモリ上に画像データを保持する。
    /// </summary>
    public byte[]? CoverImageData
    {
        get => _coverImageData;
        set
        {
            _coverImageData = value;
            IsModified = true;
            OnPropertyChanged();

            // バイナリデータからWPF表示用のBitmapImageオブジェクトを生成する。
            // CacheOption.OnLoadにより、StreamSourceのMemoryStreamが閉じられた後も
            // 画像データがメモリ上にキャッシュされ、UIで正しく表示される。
            // Freeze()を呼び出すことで、別スレッドからのアクセスも安全になる。
            if (value != null && value.Length > 0)
            {
                var image = new BitmapImage();
                using var ms = new MemoryStream(value);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze(); // スレッドセーフにするためフリーズ
                CoverImage = image;
            }
            else
            {
                CoverImage = null;
            }
        }
    }

    /// <summary>
    /// チェックボックスによる選択状態。
    /// ファイル一覧の左端のチェックボックスに対応する。
    /// 一括操作（全選択、選択除去など）で使用される。
    /// このプロパティの変更はIsModifiedに影響しない。
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    /// <summary>
    /// タグ情報が変更されたかどうかを示すフラグ。
    /// タイトル、アーティスト、アルバム等のプロパティが変更されると自動的にtrueになる。
    /// ファイルの読み込み直後はfalseにリセットされる。
    /// 保存処理が完了するとfalseに戻る。
    /// trueの場合、ファイル一覧で太字表示され、保存ボタンが有効になる。
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set => SetField(ref _isModified, value);
    }

    /// <summary>
    /// プロパティが変更されたときに発火するイベント。
    /// WPFのデータバインディングエンジンがこのイベントを監視し、
    /// UI要素の表示を自動的に更新する。
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// PropertyChangedイベントを発火させるヘルパーメソッド。
    /// [CallerMemberName]属性により、呼び出し元のプロパティ名が自動的に取得される。
    /// </summary>
    /// <param name="propertyName">変更されたプロパティ名（自動取得）</param>
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    /// <summary>
    /// バッキングフィールドの値を更新し、変更があればPropertyChangedイベントを発火する。
    /// 値が同一の場合は何もせずfalseを返す（無限ループ防止と不要な通知の抑制）。
    /// </summary>
    /// <typeparam name="T">プロパティの型</typeparam>
    /// <param name="field">バッキングフィールドへの参照</param>
    /// <param name="value">新しい値</param>
    /// <param name="propertyName">プロパティ名（自動取得）</param>
    /// <returns>値が変更された場合はtrue、変更がなかった場合はfalse</returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
