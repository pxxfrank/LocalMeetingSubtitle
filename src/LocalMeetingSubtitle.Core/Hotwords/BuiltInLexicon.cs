using LocalMeetingSubtitle.Core.Models;

namespace LocalMeetingSubtitle.Core.Hotwords;

/// <summary>A named group of lexicon terms (used for display and documentation).</summary>
public sealed record LexiconCategory(string Name, IReadOnlyList<string> Terms);

/// <summary>
/// A built-in, editable glossary of Huawei-domain terminology, used to bias recognition
/// (model-level hotwords) and to correct the most predictable homophone mistakes.
///
/// Provenance: these are <b>public</b> product/technology/process names gathered from公开资料, not an
/// internal Huawei termbase. It is intentionally easy to extend — the user's own hotwords and
/// correction rules are merged on top, and the whole lexicon can be switched off in Settings.
///
/// The bundled 14M streaming model is small, so a domain lexicon is the single cheapest accuracy
/// lever; a larger model (see the catalog) raises the ceiling further.
/// </summary>
public static class BuiltInLexicon
{
    /// <summary>Terms grouped by domain (order is display order).</summary>
    public static IReadOnlyList<LexiconCategory> Categories { get; } = new[]
    {
        new LexiconCategory("芯片与算力 / Chips & compute", new[]
        {
            "昇腾", "鲲鹏", "麒麟", "巴龙", "天罡", "鸿鹄", "凌霄", "昇思", "毕昇", "达芬奇架构",
            "昇腾AI", "Atlas", "CANN", "MDC", "泰山", "盘古", "灵衢", "海思", "算力集群", "异构计算",
            "训推一体", "端侧推理", "模型压缩", "算子", "算子库"
        }),
        new LexiconCategory("操作系统与基础软件 / OS & base software", new[]
        {
            "鸿蒙", "开源鸿蒙", "欧拉", "openEuler", "高斯", "GaussDB", "方舟编译器", "毕昇编译器",
            "分布式软总线", "超级终端", "元服务", "ArkTS", "ArkUI", "微内核", "鸿蒙生态", "鸿蒙原生"
        }),
        new LexiconCategory("云计算与数据 / Cloud & data", new[]
        {
            "华为云", "公有云", "私有云", "混合云", "HCSO", "云原生", "容器", "微服务", "应用使能",
            "数据使能", "ModelArts", "MindSpore", "云容器引擎", "云数据库", "数据湖", "湖仓一体",
            "大模型", "向量数据库", "数据治理", "多云"
        }),
        new LexiconCategory("网络与联接 / Network & connectivity", new[]
        {
            "5G", "5.5G", "6G", "Massive MIMO", "光网络", "全光网", "OTN", "路由器", "交换机", "基站",
            "核心网", "承载网", "城域网", "智能运维", "自动驾驶网络", "无线接入", "频谱", "切片"
        }),
        new LexiconCategory("终端与消费 / Devices & consumer", new[]
        {
            "智慧屏", "折叠屏", "智能穿戴", "超级快充", "分布式", "多设备协同", "eSIM", "全屋智能",
            "多屏协同", "运动健康"
        }),
        new LexiconCategory("智能汽车 / Intelligent automotive", new[]
        {
            "鸿蒙座舱", "乾崑", "途灵", "智能驾驶", "智能座舱", "车控", "车机", "域控制器"
        }),
        new LexiconCategory("数字能源 / Digital power", new[]
        {
            "数字能源", "智能光伏", "光储", "站点能源", "数据中心能源", "储能"
        }),
        new LexiconCategory("流程与组织 / Process & organisation", new[]
        {
            "IPD", "LTC", "ITR", "心声社区", "华为大学", "2012实验室", "诺亚方舟实验室",
            "战略预备队", "蓝血十杰", "三丫坡", "松山湖", "溪流背坡村", "业务连续性"
        })
    };

    /// <summary>Flat, de-duplicated hotword list.</summary>
    public static IReadOnlyList<string> Hotwords { get; } =
        Categories.SelectMany(c => c.Terms)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    public static int HotwordCount => Hotwords.Count;

    /// <summary>
    /// Conservative correction rules: only near-certain ASR mis-hearings of a term are hard-substituted.
    /// Ambiguous homophones that are themselves valid Chinese words (e.g. 高斯, 盘古, 乾坤, 灵渠) are
    /// deliberately <b>not</b> substituted — the hotword list handles those by biasing the model
    /// instead, so the exported text is never mangled by an over-eager rule.
    ///
    /// <para><b>Why <c>WholeTokenOnly = false</c>:</b> the engine's whole-token mode guards a match with
    /// <c>\p{L}</c> boundaries, and CJK characters are <c>\p{L}</c>, so a whole-token CJK rule can only
    /// fire on a standalone phrase, never inside a sentence
    /// (<c>TextCorrectionEngineTests.ChinesePhrase_IsReplacedInsideSentence</c>). These rules therefore
    /// use substring mode — still capped at 64 replacements per rule, in a single pass. The trade-off is
    /// that a legitimate word containing the wrong form (e.g. 经济升腾) is also rewritten; that is
    /// acceptable because the wrong forms are rare in meeting content, the whole lexicon can be switched
    /// off in Settings, and every rule is user-editable.</para>
    /// </summary>
    public static IReadOnlyList<TextCorrectionRule> Rules { get; } = BuildRules();

    private static IReadOnlyList<TextCorrectionRule> BuildRules()
    {
        (string Wrong, string Right)[] pairs =
        {
            ("升腾", "昇腾"),
            ("圣腾", "昇腾"),
            ("鲲朋", "鲲鹏"),
            ("昆鹏", "鲲鹏"),
            ("鸿盟", "鸿蒙"),
            ("红盟", "鸿蒙"),
            ("升思", "昇思"),
            ("毕升", "毕昇"),
            ("天刚", "天罡"),
            ("鸿湖", "鸿鹄"),
            ("灵宵", "凌霄"),
            ("巴隆", "巴龙"),
            ("骑麟", "麒麟"),
            ("新声社区", "心声社区"),
            ("嵩山湖", "松山湖")
        };

        var rules = new List<TextCorrectionRule>(pairs.Length);
        for (var i = 0; i < pairs.Length; i++)
        {
            // Higher priority runs first. Substring mode is required for CJK: the whole-token boundary
            // is \p{L}, and CJK characters are \p{L}, so a whole-token CJK rule could never fire inside
            // a sentence (see TextCorrectionEngineTests.ChinesePhrase_IsReplacedInsideSentence).
            rules.Add(TextCorrectionRule.Create(pairs[i].Wrong, pairs[i].Right, wholeTokenOnly: false));
            rules[^1].Priority = pairs.Length - i;
        }

        return rules;
    }
}
