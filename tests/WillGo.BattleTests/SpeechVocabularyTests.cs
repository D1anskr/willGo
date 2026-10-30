using System.Diagnostics;
using System.Text.Json;
using FloatingDeskAssistant.Application;

internal static class SpeechVocabularyTests
{
    public static void Run(Action<bool, string> check)
    {
        using var resource = typeof(SpeechTechnicalVocabulary).Assembly.GetManifestResourceStream("FloatingDeskAssistant.SpeechTechnicalVocabulary.json")!;
        using var document = JsonDocument.Parse(resource);
        var categories = document.RootElement.GetProperty("categories").EnumerateObject().ToArray();
        var expectedTerms = categories.SelectMany(category => category.Value.EnumerateArray()).Select(value => value.GetString()!)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        check(categories.Length >= 22 && SpeechTechnicalVocabulary.Terms.Count >= 3000, "embedded protection vocabulary covers 22 backend and AI interview categories with over 3000 distinct terms");
        check(SpeechTechnicalVocabulary.Terms.SequenceEqual(expectedTerms), "every built-in term loads without the streaming decoder's 100-hotword limit");
        var protection = new SpeechTermProtection("");
        foreach (var category in categories)
        {
            var failures = category.Value.EnumerateArray().Select(value => value.GetString()!).Where(term =>
            {
                var original = "Explain " + term;
                return protection.SelectFinalText(original, "Explain 中文替代") != original;
            }).ToArray();
            check(failures.Length == 0, $"all {category.Name} terms preserve English before question generation" +
                (failures.Length == 0 ? "" : ": " + string.Join(", ", failures.Take(5))));
        }
        foreach (var term in new[] { "ConcurrentHashMap", "AQS", "ThreadLocal", "MVCC", "InnoDB", "Redisson", "RabbitMQ", "Dubbo", "Netty", "HTTP/2", "Kubernetes", "LangChain4j", "Spring AI", "LangGraph", "MCP", "A2A", "ReAct", "RAG", "GraphRAG", "bge-m3", "Qwen3-Embedding", "HNSW", "FAISS", "Reranker", "LoRA", "QLoRA", "GRPO", "vLLM", "SGLang", "PagedAttention", "KV Cache", "GGUF", "RAGAS", "SWE-bench" })
            check(protection.SelectFinalText("Explain " + term.ToLowerInvariant(), "Explain 中文替代") == "Explain " + term.ToLowerInvariant(), "built-in case-insensitive protection: " + term);

        check(protection.SelectFinalText("请解释 frobnicate", "请解释弗罗布尼凯特") == "请解释 frobnicate", "Chinese questions preserve uncatalogued lowercase English terms");
        check(protection.SelectFinalText("Explain NewPrivateAgentRuntime", "Explain 新运行时") == "Explain NewPrivateAgentRuntime", "uncatalogued CamelCase identifiers survive in all-English questions");
        check(protection.SelectFinalText("解释 foo_bar、foo.bar、foo-bar 和 std::vector", "解释这些名字") == "解释 foo_bar、foo.bar、foo-bar 和 std::vector", "identifier fallback preserves underscore, dot, hyphen and namespace syntax");
        check(protection.SelectFinalText("解释 vendorx/v2 和 vendor_agent_v9", "解释版本和智能体") == "解释 vendorx/v2 和 vendor_agent_v9", "identifier fallback covers unknown names with paths and version numbers");
        check(protection.SelectFinalText("Explain _new_private_agent and 7BPrivateModel", "Explain 新模型") == "Explain _new_private_agent and 7BPrivateModel", "identifier fallback covers leading underscores and leading numeric model sizes");
        check(protection.SelectFinalText("问题 12345", "问题一万二千三百四十五") == "问题一万二千三百四十五", "generic identifier fallback does not freeze ordinary numeric text");
        check(protection.SelectFinalText("解释 SQL", "解释 SQL injection") == "解释 SQL injection", "a longer refined phrase still counts its retained shorter English term");
        check(protection.SelectFinalText("Explain API", "Explain APIs") == "Explain API", "technical term boundaries reject a different larger word");
        check(protection.SelectFinalText("请解释 kafka_client", "请解释 kafka_client 的作用？") == "请解释 kafka_client 的作用？", "longer identifiers can keep refinement improvements without matching an inner hotword");
        check(protection.SelectFinalText("Explain M C P", "Explain 模型上下文协议") == "Explain MCP", "spelled built-in acronym letters normalize without a custom hotword");
        check(protection.SelectFinalText("什么是 Z", "什么是 Z？") == "什么是 Z？" && protection.SelectFinalText("什么是 ZG", "什么是 ZG？") == "什么是 ZG？", "expanded vocabulary never invents a missing ZGC letter");
        check(protection.SelectFinalText("I wnat a good answer", "I want a good answer.") == "I want a good answer.", "ordinary English spelling corrections remain available");
        check(protection.SelectFinalText("什么事缓存", "什么是缓存？") == "什么是缓存？", "Chinese-only sentences retain whole-utterance correction");
        check(protection.SelectFinalText("读过卡夫卡吗", "读过卡夫卡吗？") == "读过卡夫卡吗？", "expanded protection does not rewrite legitimate Chinese mentions");
        check(protection.SelectFinalText("Explain LangGraph", "Explain LangGraph.") == "Explain LangGraph.", "preserved built-in terms allow punctuation corrections");
        check(protection.SelectFinalText("LangGraph and LangGraph", "LangGraph and 中文替代") == "LangGraph and LangGraph", "repeated built-in terms retain every occurrence");
        check(protection.SelectFinalText("Explain C++ and C# and .NET", "Explain C and C and NET") == "Explain C++ and C# and .NET", "punctuated language and framework names remain distinct");

        var custom = string.Join("\n", Enumerable.Range(0, 150).Select(i => "customword" + i)) + "\nzyxphonixconcept";
        check(SpeechHotwords.Parse(custom).Length == 100, "decoder hotword list remains bounded independently of output protection");
        check(new SpeechTermProtection(custom).SelectFinalText("Explain zyxphonixconcept", "Explain 中文替代") == "Explain zyxphonixconcept", "custom protection terms after position 100 are also retained");
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 200; i++) protection.SelectFinalText("什么是 LangGraph，MCP 和 vLLM 的 KV Cache 有什么区别", "什么是 LangGraph，MCP 和 vLLM 的 KV Cache 有什么区别？");
        watch.Stop();
        check(watch.Elapsed.TotalMilliseconds < 2000, "large-vocabulary guard processes 200 mixed technical utterances in under 2 seconds");
        Console.WriteLine($"VOCABULARY: {categories.Length} categories, {SpeechTechnicalVocabulary.Terms.Count} unique terms; warm selection {watch.Elapsed.TotalMilliseconds / 200:F3} ms/utterance");
    }
}
