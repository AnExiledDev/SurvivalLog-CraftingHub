using System;
using System.Globalization;
using System.IO;
using CraftingHub.Core;
using GameCore.HotUpdate;
using GameCore.HotUpdate.ReduxUI;

namespace CraftingHub.Adapters;

/// <summary>
/// page.js, run in the root document the game's pages sit in, the way Storage Hub and the ivmakk mods add their UI.
/// The web view can reload at any time and lose the script, so it is checked for every couple of seconds and put
/// back when the page says it is missing.
/// </summary>
internal sealed class WorkbenchPage
{
    private const float CheckSeconds = 2f;
    private const string ScriptResource = "CraftingHub.page.js";
    private const string NoScriptReply = "window.vuplex&&window.vuplex.postMessage('" + PageCommand.Prefix + "noscript')";

    private readonly Action<string> _log;
    private string? _script;
    private float _nextCheckAt;
    private bool _hasReportedRunFailure;

    public WorkbenchPage(Action<string> log)
    {
        _log = log;
    }

    public void KeepInstalled(float now)
    {
        if (now < _nextCheckAt) return;

        _nextCheckAt = now + CheckSeconds;
        Run("window.__craftinghub||" + NoScriptReply);
    }

    public void Install()
    {
        if (Run(Script())) _log("page script sent to the web UI");
    }

    public void Show(string viewJson) => Call($"setData({viewJson})");

    public void ClickRecipe(string key) => Call($"act('recipe',{HubView.Str(key)})");

    public void Craft(int quantity) => Call($"act('craft',{quantity.ToString(CultureInfo.InvariantCulture)})");

    public void ConfirmResult() => Call("act('confirm')");

    private void Call(string expression) => Run($"window.__craftinghub?window.__craftinghub.{expression}:{NoScriptReply}");

    private bool Run(string javaScript)
    {
        var webView = BaseSingleton<ReduxUISystem>.Instance?.GetWebUILayer()?.canvasWebViewPrefab?.WebView;

        if (webView == null) return false;

        try
        {
            webView.ExecuteJavaScript(javaScript, null);

            return true;
        }
        catch (Exception ex)
        {
            if (!_hasReportedRunFailure) _log("could not run script in the web UI (reported once): " + ex.Message);

            _hasReportedRunFailure = true;

            return false;
        }
    }

    private string Script()
    {
        if (_script != null) return _script;

        using var stream = typeof(WorkbenchPage).Assembly.GetManifestResourceStream(ScriptResource) ?? throw new InvalidOperationException(ScriptResource + " is not embedded");
        using var reader = new StreamReader(stream);

        return _script = reader.ReadToEnd();
    }
}
