using Mp3TagEditor.Models;
using System.IO;
using System.Windows.Media.Imaging;
using TagLib;

namespace Mp3TagEditor.Services;

/// <summary>
/// TagLibSharpライブラリを使用してMP3ファイルのID3タグを読み書きするサービスクラス。
///
/// このクラスはstaticメソッドのみで構成され、インスタンスを生成せずに使用する。
/// TagLibSharpは、MP3ファイルのID3v1/ID3v2タグに対応しており、
/// タイトル、アーティスト、アルバム、カバー画像などのメタデータを操作できる。
///
/// 主な機能：
/// - ReadTags: MP3ファイルからタグ情報を読み込んでMp3FileInfoオブジェクトを生成
/// - WriteTags: Mp3FileInfoの内容をMP3ファイルのタグに書き込み
/// - GetMp3Files: フォルダ内のMP3ファイルを再帰的に検索
/// </summary>
public static class TagService
{
    /// <summary>
    /// 指定されたMP3ファイルからID3タグ情報を読み込み、Mp3FileInfoオブジェクトを返す。
    ///
    /// TagLibSharpのFile.Createメソッドでファイルを開き、各タグフィールドを読み取る。
    /// タグが未設定（null）のフィールドは空文字列に変換される。
    /// カバー画像が埋め込まれている場合は、最初の画像のバイナリデータを取得する。
    ///
    /// 読み込み完了後、IsModifiedフラグをfalseにリセットし、
    /// 読み込み時のプロパティ設定が「変更」として扱われないようにする。
    /// </summary>
    /// <param name="filePath">読み込むMP3ファイルの絶対パス</param>
    /// <returns>タグ情報を格納したMp3FileInfoオブジェクト</returns>
    /// <exception cref="TagLib.CorruptFileException">ファイルが破損している場合</exception>
    /// <exception cref="FileNotFoundException">ファイルが存在しない場合</exception>
    public static Mp3FileInfo ReadTags(string filePath)
    {
        // TagLibSharpでMP3ファイルを開く（usingで自動的にリソースを解放）
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;

        // Mp3FileInfoオブジェクトを生成し、ファイルパスを設定
        var info = new Mp3FileInfo
        {
            FilePath = filePath,
        };

        // 各タグフィールドを読み込む。
        // nullの場合は空文字列に変換し、UIでnull参照エラーが発生しないようにする。
        // FirstPerformer/FirstAlbumArtist/FirstGenreは、
        // 配列の最初の要素を返すTagLibSharpのヘルパープロパティ。
        info.Title = tag.Title ?? string.Empty;
        info.Artist = tag.FirstPerformer ?? string.Empty;
        info.Album = tag.Album ?? string.Empty;
        info.AlbumArtist = tag.FirstAlbumArtist ?? string.Empty;
        info.TrackNumber = tag.Track;
        info.Year = tag.Year;
        info.Genre = tag.FirstGenre ?? string.Empty;

        // カバー画像（APIC: Attached Picture）を読み込む。
        // MP3ファイルには複数の画像を埋め込めるが、ここでは最初の1枚のみを使用する。
        // Pictures[0].Data.Dataでバイナリデータ（byte[]）を取得する。
        if (tag.Pictures.Length > 0)
        {
            var picture = tag.Pictures[0];
            info.CoverImageData = picture.Data.Data;
        }

        // プロパティ設定時にIsModifiedがtrueになるため、
        // 読み込み完了後にfalseにリセットして「未変更」状態にする。
        info.IsModified = false;

        return info;
    }

    /// <summary>
    /// Mp3FileInfoオブジェクトの内容をMP3ファイルのID3タグに書き込む。
    ///
    /// TagLibSharpでファイルを開き、各フィールドを設定してからSave()で保存する。
    /// Performers、AlbumArtists、Genresは文字列配列型のため、
    /// 空でない場合は1要素の配列に変換して設定する。
    ///
    /// カバー画像はPictureType.FrontCover（フロントカバー）として設定される。
    /// 画像データがnullまたは空の場合は、既存の画像を削除する。
    ///
    /// 保存成功後、IsModifiedフラグをfalseにリセットする。
    /// </summary>
    /// <param name="info">書き込むタグ情報を持つMp3FileInfoオブジェクト</param>
    /// <exception cref="TagLib.CorruptFileException">ファイルが破損している場合</exception>
    /// <exception cref="UnauthorizedAccessException">ファイルへの書き込み権限がない場合</exception>
    public static void WriteTags(Mp3FileInfo info)
    {
        // TagLibSharpでMP3ファイルを開く
        using var file = TagLib.File.Create(info.FilePath);
        var tag = file.Tag;

        // 基本的なタグフィールドを設定
        tag.Title = info.Title;

        // Performers（演奏者）は文字列配列型。
        // 空の場合は空配列を設定し、既存の値をクリアする。
        tag.Performers = string.IsNullOrEmpty(info.Artist)
            ? []
            : [info.Artist];

        tag.Album = info.Album;

        // AlbumArtists（アルバムアーティスト）も文字列配列型
        tag.AlbumArtists = string.IsNullOrEmpty(info.AlbumArtist)
            ? []
            : [info.AlbumArtist];

        tag.Track = info.TrackNumber;
        tag.Year = info.Year;

        // Genres（ジャンル）も文字列配列型
        tag.Genres = string.IsNullOrEmpty(info.Genre)
            ? []
            : [info.Genre];

        // カバー画像の書き込み。
        // PictureType.FrontCoverは、音楽プレイヤーで表示される標準的なカバー画像タイプ。
        // MimeTypeは"image/jpeg"を指定（PNGの場合も多くのプレイヤーで正しく表示される）。
        if (info.CoverImageData != null && info.CoverImageData.Length > 0)
        {
            var picture = new Picture(new ByteVector(info.CoverImageData))
            {
                Type = PictureType.FrontCover,   // フロントカバー（アルバムジャケット表面）
                MimeType = "image/jpeg",          // MIME型
                Description = "Cover"             // 画像の説明文
            };
            tag.Pictures = [picture];
        }
        else
        {
            // 画像データがない場合は、既存の埋め込み画像をすべて削除
            tag.Pictures = [];
        }

        // 変更内容をファイルに書き込む
        file.Save();

        // 保存成功後、変更フラグをリセット
        info.IsModified = false;
    }

    /// <summary>
    /// 指定されたフォルダ配下のすべてのMP3ファイルのパスを再帰的に取得する。
    /// サブフォルダ内のファイルも含めて検索し、パス名の昇順でソートして返す。
    /// </summary>
    /// <param name="folderPath">検索するフォルダの絶対パス</param>
    /// <returns>MP3ファイルパスのコレクション（昇順ソート済み）</returns>
    public static IEnumerable<string> GetMp3Files(string folderPath)
    {
        return Directory.EnumerateFiles(folderPath, "*.mp3", SearchOption.AllDirectories)
            .OrderBy(f => f);
    }
}
