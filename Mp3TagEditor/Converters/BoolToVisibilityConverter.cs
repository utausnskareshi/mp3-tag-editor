using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Mp3TagEditor.Converters;

/// <summary>
/// bool値をWPFのVisibility列挙型に変換するコンバーター。
///
/// WPFのデータバインディングでは、bool型のプロパティを直接Visibilityプロパティに
/// バインドできないため、このコンバーターを介して変換を行う。
///
/// 変換ルール：
/// - true  → Visibility.Visible（要素を表示）
/// - false → Visibility.Collapsed（要素を非表示にし、レイアウトスペースも占有しない）
///
/// XAMLでの使用例：
///   Visibility="{Binding IsBusy, Converter={StaticResource BoolToVis}}"
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// bool値をVisibilityに変換する（ViewModel → View方向）。
    /// </summary>
    /// <param name="value">バインディングソースの値（bool型を期待）</param>
    /// <param name="targetType">変換先の型（Visibility）</param>
    /// <param name="parameter">コンバーターパラメータ（未使用）</param>
    /// <param name="culture">カルチャ情報（未使用）</param>
    /// <returns>trueならVisible、falseまたはbool以外ならCollapsed</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
            return b ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    /// <summary>
    /// VisibilityをboolKに逆変換する（View → ViewModel方向）。
    /// 双方向バインディング時に使用される。
    /// </summary>
    /// <param name="value">Visibility値</param>
    /// <param name="targetType">変換先の型（bool）</param>
    /// <param name="parameter">コンバーターパラメータ（未使用）</param>
    /// <param name="culture">カルチャ情報（未使用）</param>
    /// <returns>VisibleならTrue、それ以外はfalse</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// null/非null値をVisibilityに変換するコンバーター。
///
/// オブジェクトがnullかどうかに応じて要素の表示/非表示を切り替える。
/// 主にカバー画像や検索結果リストの表示制御に使用される。
///
/// 変換ルール：
/// - 非null → Visibility.Visible（要素を表示）
/// - null   → Visibility.Collapsed（要素を非表示）
///
/// XAMLでの使用例：
///   Visibility="{Binding CoverImage, Converter={StaticResource NullToVis}}"
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// null/非null値をVisibilityに変換する。
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// 逆変換は未サポート（一方向バインディング専用）。
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// bool値をWPFのFontWeight（フォントの太さ）に変換するコンバーター。
///
/// ファイル一覧のDataGridで、変更済みのファイル（IsModified=true）の行を
/// 太字（Bold）で表示するために使用される。
/// 未変更のファイルは通常の太さ（Normal）で表示される。
///
/// 変換ルール：
/// - true  → FontWeights.Bold（太字）
/// - false → FontWeights.Normal（通常）
///
/// XAMLでの使用例：
///   FontWeight="{Binding IsModified, Converter={StaticResource BoolToWeight}}"
/// </summary>
public class BoolToFontWeightConverter : IValueConverter
{
    /// <summary>
    /// bool値をFontWeightに変換する。
    /// </summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return FontWeights.Bold;
        return FontWeights.Normal;
    }

    /// <summary>
    /// 逆変換は未サポート（一方向バインディング専用）。
    /// </summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
