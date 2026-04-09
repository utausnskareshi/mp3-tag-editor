using Mp3TagEditor.Models;
using System.Net.Http;
using System.Text.Json;

namespace Mp3TagEditor.Services;

/// <summary>
/// iTunes Search APIを使用して楽曲情報を検索するサービスクラス。
///
/// Apple が提供する iTunes Search API（https://itunes.apple.com/search）を利用して、
/// 楽曲のメタデータ（曲名、アーティスト、アルバム、ジャンル、アートワーク等）を取得する。
/// APIキーは不要で、無料で利用できる。
///
/// 邦楽（日本人アーティスト）の検索精度を高めるため、以下の戦略を採用している：
/// 1. まず日本のiTunes Store（country=JP）で検索し、日本語楽曲を優先的にヒットさせる
/// 2. JPで見つからない場合はUS（グローバル）にフォールバックする
/// 3. アーティスト名＋曲名で検索し、ヒットしなければ曲名のみで再検索する
///
/// API制限について：
/// iTunes Search APIには明確なレート制限の公式ドキュメントはないが、
/// 短時間に大量のリクエストを送るとブロックされる可能性がある。
/// そのため一括取得時は各リクエスト間に500msの待機時間を設けている。
/// </summary>
public class ITunesSearchService : IDisposable
{
    /// <summary>HTTP通信用のクライアント（インスタンス全体で再利用）</summary>
    private readonly HttpClient _httpClient;

    /// <summary>iTunes Search APIのベースURL</summary>
    private const string BaseUrl = "https://itunes.apple.com/search";

    /// <summary>
    /// コンストラクタ。HttpClientを初期化し、User-Agentヘッダーを設定する。
    /// User-Agentを設定しないとAPIがリクエストを拒否する場合があるため、
    /// アプリケーション名を明示的に指定する。
    /// </summary>
    public ITunesSearchService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mp3TagEditor/1.0");
    }

    /// <summary>
    /// 任意の検索クエリで楽曲を検索する。
    ///
    /// 検索フロー：
    /// 1. まず日本のiTunes Store（country=JP）で検索を実行
    /// 2. 結果が1件以上あればそれを返す（邦楽が優先的にヒット）
    /// 3. JPで結果が0件の場合、US（グローバル）で再検索
    ///
    /// この2段階検索により、邦楽は日本語の正確な情報が取得でき、
    /// 洋楽やグローバルな楽曲もフォールバックで対応できる。
    /// </summary>
    /// <param name="query">検索クエリ文字列（アーティスト名、曲名、またはその組み合わせ）</param>
    /// <param name="cancellationToken">キャンセルトークン（ユーザーが処理を中断する場合に使用）</param>
    /// <returns>検索結果のITunesTrackリスト（0件の場合は空リスト）</returns>
    public async Task<List<ITunesTrack>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        // まず日本のiTunes Storeで検索（邦楽の検索精度が高い）
        var results = await SearchInternalAsync(query, "JP", cancellationToken);
        if (results.Count > 0)
            return results;

        // 日本で見つからなければグローバル（US）で検索
        return await SearchInternalAsync(query, "US", cancellationToken);
    }

    /// <summary>
    /// アーティスト名と曲名を個別に指定して検索する。
    /// 単純なクエリ結合よりも高い精度で楽曲を特定できる。
    ///
    /// 検索フロー：
    /// 1. アーティスト名＋曲名を結合してSearchAsyncで検索
    /// 2. 結果が0件の場合、曲名のみで再検索
    ///    （アーティスト名の表記揺れに対応。例: "髭男" vs "Official髭男dism"）
    /// </summary>
    /// <param name="artist">アーティスト名（空でも可）</param>
    /// <param name="title">曲名（空でも可）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果のITunesTrackリスト</returns>
    public async Task<List<ITunesTrack>> SearchByArtistAndTitleAsync(
        string artist, string title, CancellationToken cancellationToken = default)
    {
        // アーティスト名と曲名をスペースで結合して検索
        var query = $"{artist} {title}";
        var results = await SearchAsync(query, cancellationToken);

        if (results.Count > 0)
            return results;

        // アーティスト名を含めた検索で見つからない場合、曲名のみで再検索。
        // これにより、アーティスト名の表記が異なる場合（略称、英語表記等）でも
        // 曲名だけでヒットする可能性を高める。
        if (!string.IsNullOrWhiteSpace(title))
        {
            results = await SearchAsync(title, cancellationToken);
        }

        return results;
    }

    /// <summary>
    /// ファイル名から検索クエリを自動生成して検索する。
    /// MP3ファイルにタグ情報が一切設定されていない場合に使用される。
    ///
    /// CleanFileNameメソッドでファイル名をクリーンアップし、
    /// トラック番号や特殊文字などの検索ノイズを除去してから検索する。
    /// </summary>
    /// <param name="fileName">MP3ファイル名（拡張子含む）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果のITunesTrackリスト</returns>
    public async Task<List<ITunesTrack>> SearchByFileNameAsync(
        string fileName, CancellationToken cancellationToken = default)
    {
        // ファイル名をクリーンアップして検索に適した文字列に変換
        var query = CleanFileName(fileName);
        return await SearchAsync(query, cancellationToken);
    }

    /// <summary>
    /// 指定されたURLからアルバムアートワーク画像をダウンロードする。
    /// ダウンロードした画像はバイナリデータ（byte[]）として返される。
    ///
    /// ネットワークエラーやURLが無効な場合はnullを返す（例外は投げない）。
    /// </summary>
    /// <param name="artworkUrl">アートワーク画像のURL</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>画像のバイナリデータ、またはnull（ダウンロード失敗時）</returns>
    public async Task<byte[]?> DownloadArtworkAsync(
        string? artworkUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(artworkUrl))
            return null;

        try
        {
            return await _httpClient.GetByteArrayAsync(artworkUrl, cancellationToken);
        }
        catch
        {
            // ネットワークエラー、タイムアウト、404等の場合はnullを返す。
            // アートワークの取得失敗は致命的ではないため、例外は握りつぶす。
            return null;
        }
    }

    /// <summary>
    /// iTunes Search APIに対してHTTPリクエストを送信し、検索結果を取得する内部メソッド。
    ///
    /// APIパラメータ：
    /// - term: 検索キーワード（URLエンコード済み）
    /// - country: 国コード（"JP"=日本、"US"=アメリカ）。検索対象のiTunes Storeを指定
    /// - media: メディアタイプ。"music"で音楽のみに限定
    /// - entity: エンティティタイプ。"song"で楽曲のみに限定（アルバム・アーティストを除外）
    /// - limit: 返却件数の上限（最大200、ここでは10件に設定）
    /// </summary>
    /// <param name="query">検索クエリ文字列</param>
    /// <param name="country">国コード（"JP" または "US"）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検索結果のITunesTrackリスト（エラー時は空リスト）</returns>
    private async Task<List<ITunesTrack>> SearchInternalAsync(
        string query, string country, CancellationToken cancellationToken)
    {
        // 空クエリの場合は検索せずに空リストを返す
        if (string.IsNullOrWhiteSpace(query))
            return [];

        // クエリ文字列をURLエンコードし、APIのURLを構築
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"{BaseUrl}?term={encodedQuery}&country={country}&media=music&entity=song&limit=10";

        try
        {
            // HTTPリクエストを送信し、JSONレスポンスを取得
            var response = await _httpClient.GetStringAsync(url, cancellationToken);

            // JSONレスポンスをITunesSearchResponseオブジェクトにデシリアライズ
            var searchResponse = JsonSerializer.Deserialize<ITunesSearchResponse>(response);
            return searchResponse?.Results ?? [];
        }
        catch (Exception)
        {
            // ネットワークエラー、JSONパースエラー等の場合は空リストを返す。
            // 検索失敗は致命的ではなく、UIにはステータスメッセージで通知される。
            return [];
        }
    }

    /// <summary>
    /// MP3ファイル名をクリーンアップして、iTunes検索に適したクエリ文字列に変換する。
    ///
    /// 多くのMP3ファイルは以下のような命名パターンを持つ：
    /// - "01 - 夜に駆ける.mp3"（トラック番号付き）
    /// - "YOASOBI_夜に駆ける (Official Audio).mp3"（付加情報付き）
    /// - "01. Pretender [MV].mp3"（括弧内の情報付き）
    ///
    /// これらのパターンからノイズとなる部分を除去し、
    /// 「アーティスト名 曲名」に近い形のクエリを生成する。
    ///
    /// 処理ステップ：
    /// 1. 拡張子（.mp3）を除去
    /// 2. 先頭のトラック番号（"01 - ", "01. ", "1-" 等）を除去
    /// 3. 括弧内の付加情報（"(Official Audio)", "[MV]", "（公式）" 等）を除去
    /// 4. アンダースコアとハイフンをスペースに変換
    /// 5. 連続する空白を1つに正規化し、前後の空白を除去
    /// </summary>
    /// <param name="fileName">元のMP3ファイル名（拡張子含む）</param>
    /// <returns>クリーンアップされた検索クエリ文字列</returns>
    private static string CleanFileName(string fileName)
    {
        // ステップ1: 拡張子を除去
        var name = System.IO.Path.GetFileNameWithoutExtension(fileName);

        // ステップ2: 先頭のトラック番号パターンを除去
        // 対応パターン: "01 - ", "01. ", "1-", "001_" など（1〜3桁の数字＋区切り文字）
        name = System.Text.RegularExpressions.Regex.Replace(
            name, @"^\d{1,3}[\s.\-_]+", "");

        // ステップ3: 括弧（半角・全角両対応）内の付加情報を除去
        // 対応括弧: (), [], （）, 【】
        name = System.Text.RegularExpressions.Regex.Replace(
            name, @"[\(\[（【].+?[\)\]）】]", "");

        // ステップ4: アンダースコアとハイフンをスペースに変換
        name = name.Replace('_', ' ').Replace('-', ' ');

        // ステップ5: 連続する空白を1つに正規化し、前後の空白をトリム
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s+", " ").Trim();

        return name;
    }

    /// <summary>
    /// HttpClientリソースを解放する。
    /// IDisposableパターンの実装。
    /// GC.SuppressFinalizeにより、ファイナライザの呼び出しを抑制する。
    /// </summary>
    public void Dispose()
    {
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }
}
