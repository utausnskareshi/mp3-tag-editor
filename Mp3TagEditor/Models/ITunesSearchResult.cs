using System.Text.Json.Serialization;

namespace Mp3TagEditor.Models;

/// <summary>
/// iTunes Search APIのレスポンス全体を表すモデルクラス。
///
/// iTunes Search APIは以下の形式でJSONレスポンスを返す：
/// {
///   "resultCount": 10,
///   "results": [ { ... }, { ... }, ... ]
/// }
///
/// System.Text.JsonのJsonPropertyName属性を使用して、
/// JSONのキー名とC#プロパティのマッピングを定義している。
/// </summary>
public class ITunesSearchResponse
{
    /// <summary>
    /// 検索結果の件数。
    /// APIが返したトラック情報の総数を示す。
    /// </summary>
    [JsonPropertyName("resultCount")]
    public int ResultCount { get; set; }

    /// <summary>
    /// 検索結果のトラック情報リスト。
    /// 各要素はiTunes Store上の1曲に対応するITunesTrackオブジェクト。
    /// </summary>
    [JsonPropertyName("results")]
    public List<ITunesTrack> Results { get; set; } = [];
}

/// <summary>
/// iTunes Search APIから取得した個別のトラック（楽曲）情報を表すモデルクラス。
///
/// iTunes Search APIのレスポンスには多数のフィールドが含まれるが、
/// ここではMP3タグ編集に必要なフィールドのみを定義している。
///
/// 主な用途：
/// - 自動取得機能で楽曲情報をMP3タグに適用する
/// - 手動検索結果のリストに表示する
/// - アルバムアートワーク画像のURLを取得する
/// </summary>
public class ITunesTrack
{
    /// <summary>
    /// トラック名（楽曲のタイトル）。
    /// MP3タグのTitleフィールドに対応する。
    /// 例: "夜に駆ける", "Pretender"
    /// </summary>
    [JsonPropertyName("trackName")]
    public string? TrackName { get; set; }

    /// <summary>
    /// アーティスト名（演奏者・歌手名）。
    /// MP3タグのArtistフィールドに対応する。
    /// 例: "YOASOBI", "Official髭男dism"
    /// </summary>
    [JsonPropertyName("artistName")]
    public string? ArtistName { get; set; }

    /// <summary>
    /// コレクション名（アルバム名またはシングル名）。
    /// MP3タグのAlbumフィールドに対応する。
    /// 例: "THE BOOK", "Traveler"
    /// </summary>
    [JsonPropertyName("collectionName")]
    public string? CollectionName { get; set; }

    /// <summary>
    /// アルバム内でのトラック番号。
    /// MP3タグのTrackNumberフィールドに対応する。
    /// </summary>
    [JsonPropertyName("trackNumber")]
    public int TrackNumber { get; set; }

    /// <summary>
    /// リリース日（ISO 8601形式の文字列）。
    /// 例: "2020-12-01T08:00:00Z"
    /// ReleaseYearプロパティで年のみを抽出して使用する。
    /// </summary>
    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    /// <summary>
    /// 主ジャンル名。
    /// MP3タグのGenreフィールドに対応する。
    /// 例: "J-Pop", "アニメ", "ロック"
    /// </summary>
    [JsonPropertyName("primaryGenreName")]
    public string? PrimaryGenreName { get; set; }

    /// <summary>
    /// アルバムアートワーク画像のURL（100x100ピクセル）。
    /// iTunes APIが標準で返すサムネイルサイズの画像URL。
    /// 高解像度版が必要な場合はArtworkUrl600プロパティを使用する。
    /// </summary>
    [JsonPropertyName("artworkUrl100")]
    public string? ArtworkUrl100 { get; set; }

    /// <summary>
    /// アルバムアートワーク画像のURL（60x60ピクセル）。
    /// 最も小さいサムネイルサイズ。通常は使用しない。
    /// </summary>
    [JsonPropertyName("artworkUrl60")]
    public string? ArtworkUrl60 { get; set; }

    /// <summary>
    /// コレクション（アルバム）のアーティスト名。
    /// コンピレーションアルバムの場合、各トラックのアーティストとは異なることがある。
    /// MP3タグのAlbumArtistフィールドに対応する。
    /// </summary>
    [JsonPropertyName("collectionArtistName")]
    public string? CollectionArtistName { get; set; }

    /// <summary>
    /// iTunes Store上のトラック固有ID。
    /// 楽曲を一意に識別するために使用される。
    /// </summary>
    [JsonPropertyName("trackId")]
    public long TrackId { get; set; }

    /// <summary>
    /// iTunes Store上のコレクション（アルバム）固有ID。
    /// アルバムを一意に識別するために使用される。
    /// </summary>
    [JsonPropertyName("collectionId")]
    public long CollectionId { get; set; }

    /// <summary>
    /// リリース年を数値で取得する算出プロパティ。
    /// ReleaseDateの文字列からDateTimeにパースし、年の部分のみを抽出する。
    /// パースに失敗した場合は0を返す。
    /// MP3タグのYearフィールドに対応する。
    /// </summary>
    public uint ReleaseYear
    {
        get
        {
            if (DateTime.TryParse(ReleaseDate, out var date))
                return (uint)date.Year;
            return 0;
        }
    }

    /// <summary>
    /// 高解像度のアルバムアートワークURL（600x600ピクセル）を生成する算出プロパティ。
    ///
    /// iTunes APIが返すArtworkUrl100の画像サイズ指定部分（"100x100bb"）を
    /// "600x600bb"に置換することで、より高解像度の画像URLを生成する。
    /// この仕組みはiTunes APIの非公式だが広く知られた仕様に基づいている。
    ///
    /// MP3ファイルに埋め込むカバー画像として十分な解像度を確保するため、
    /// デフォルトの100x100ではなくこの600x600版を使用する。
    /// </summary>
    public string? ArtworkUrl600 =>
        ArtworkUrl100?.Replace("100x100bb", "600x600bb");

    /// <summary>
    /// 手動検索結果のリスト表示用の文字列表現。
    /// 「曲名 - アーティスト名 (アルバム名)」の形式で返す。
    /// </summary>
    public override string ToString() =>
        $"{TrackName} - {ArtistName} ({CollectionName})";
}
