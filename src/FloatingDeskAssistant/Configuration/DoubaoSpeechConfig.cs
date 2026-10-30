namespace FloatingDeskAssistant.Configuration;

public sealed class DoubaoSpeechConfig
{
    public string ApiKey { get; set; } = "";
    public string AppId { get; set; } = "";
    public string AccessToken { get; set; } = "";

    public DoubaoSpeechConfig Clone() => new()
    {
        ApiKey = ApiKey ?? "", AppId = AppId ?? "", AccessToken = AccessToken ?? ""
    };

    public void Validate(bool requireCredentials = true)
    {
        if (requireCredentials && string.IsNullOrWhiteSpace(ApiKey) && (string.IsNullOrWhiteSpace(AppId) || string.IsNullOrWhiteSpace(AccessToken)))
            throw new InvalidOperationException("请先在语音设置中填写豆包语音接口密钥，或旧版服务的应用标识与访问令牌。请开通豆包流式语音识别模型 2.0（小时版）。");
        foreach (var value in new[] { ApiKey, AppId, AccessToken })
            if (value?.Any(char.IsControl) == true) throw new ArgumentException("豆包语音凭证不能包含换行或控制字符。");
    }
}
