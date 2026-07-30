using System.Net.Http;
using System.Threading.Tasks;
using EiTRVO.ProEngine.Orchestrators;
using EiTRVO.ProEngine.Services;
using EiTRVO.ProEngine.ViewModels;
using EiTRVO.Tests.Fakes;

namespace EiTRVO.Tests.ViewModels;

[TestClass]
public class AccountViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FakeGameFolderService _gameFolder;
    private readonly FakeNotificationService _notification;
    private readonly FakeDialogService _dialog;
    private readonly FakeClipboardService _clipboard;
    private readonly FakeProcessService _process;
    private readonly AccountManager _accountManager;
    private readonly HttpClient _httpClient;
    private readonly FakeAuthService _authService;

    public AccountViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"eitrvo_acctvm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _gameFolder = new FakeGameFolderService { GameDir = _tempDir };
        _notification = new FakeNotificationService();
        _dialog = new FakeDialogService();
        _clipboard = new FakeClipboardService();
        _process = new FakeProcessService();
        _accountManager = new AccountManager(_gameFolder);
        _httpClient = new HttpClient(
            new FakeHttpMessageHandler(System.Net.HttpStatusCode.OK, "{}"));
        _authService = new FakeAuthService();
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    [TestMethod]
    public void Constructor_InitializesDefaults()
    {
        var vm = new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);

        Assert.IsFalse(vm.IsAddingAccount);
        Assert.IsFalse(vm.IsDeviceCodeVisible);
        Assert.IsNotNull(vm.Accounts);
    }

    [TestMethod]
    public void ToggleYggdrasilForm_ShowsForm()
    {
        var vm = new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);

        vm.ToggleYggdrasilFormCommand.Execute(null);

        Assert.IsTrue(vm.IsAddingYggdrasilAccount);
        Assert.IsTrue(vm.IsYggdrasilFormVisible);
    }

    [TestMethod]
    public void CancelYggdrasil_HidesForm()
    {
        var vm = new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);

        vm.ToggleYggdrasilFormCommand.Execute(null);
        vm.CancelYggdrasilCommand.Execute(null);

        Assert.IsFalse(vm.IsAddingYggdrasilAccount);
    }

    [TestMethod]
    public void RemoveAccount_NotConfirmed_KeepsAccount()
    {
        _dialog.ConfirmResult = false;
        _accountManager.Add(new EiTRVO.ProEngine.Models.Account
        {
            Username = "KeepMe",
            UUID = "uuid-keep",
            Type = EiTRVO.ProEngine.Models.AccountType.Microsoft
        });

        var vm = new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);

        vm.RemoveAccountCommand.Execute("uuid-keep");

        Assert.IsNotNull(_accountManager.FindByUuid("uuid-keep"));
    }

    [TestMethod]
    public void RemoveAccount_Confirmed_RemovesIt()
    {
        _dialog.ConfirmResult = true;
        _accountManager.Add(new EiTRVO.ProEngine.Models.Account
        {
            Username = "RemoveMe",
            UUID = "uuid-rm",
            Type = EiTRVO.ProEngine.Models.AccountType.Microsoft
        });

        var vm = new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);

        vm.RemoveAccountCommand.Execute("uuid-rm");

        Assert.IsNull(_accountManager.FindByUuid("uuid-rm"));
    }

    // ================================================================
    // YggdrasilLogin — Security character filtering
    // ================================================================

    private AccountViewModel CreateYggdrasilTestVm()
    {
        return new AccountViewModel(
            _authService, _accountManager, _httpClient,
            _notification, _dialog, _clipboard, _process);
    }

    [TestMethod]
    public async Task YggdrasilLogin_EmptyUrl_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "请输入验证服务器 URL");
        Assert.AreEqual(EiTRVO.ProEngine.Models.NotificationType.Warning, _notification.LastShowType);
    }

    [TestMethod]
    public async Task YggdrasilLogin_WhitespaceOnlyUrl_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "   ";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "请输入验证服务器 URL");
    }

    [TestMethod]
    public async Task YggdrasilLogin_NonHttpsUrl_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "http://auth.example.com";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "https://");
        Assert.AreEqual(EiTRVO.ProEngine.Models.NotificationType.Warning, _notification.LastShowType);
    }

    [TestMethod]
    public async Task YggdrasilLogin_UrlWithEquals_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com?token=abc";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "非法字符");
        Assert.AreEqual(EiTRVO.ProEngine.Models.NotificationType.Warning, _notification.LastShowType);
    }

    [TestMethod]
    public async Task YggdrasilLogin_UrlWithSpace_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com/inject ed";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "非法字符");
    }

    [TestMethod]
    public async Task YggdrasilLogin_UrlWithDoubleQuote_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com/\"inject";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "非法字符");
    }

    [TestMethod]
    public async Task YggdrasilLogin_UrlWithSingleQuote_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com/'inject";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "非法字符");
    }

    [TestMethod]
    public async Task YggdrasilLogin_UrlWithNewline_Rejected()
    {
        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com\ninject";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "pwd";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        StringAssert.Contains(_notification.LastShowMessage!, "非法字符");
    }

    [TestMethod]
    public async Task YggdrasilLogin_ValidUrl_DoesNotTriggerSecurityWarning()
    {
        _authService.YggdrasilAuthResult = new EiTRVO.ProEngine.Models.Account
        {
            Username = "TestPlayer",
            UUID = "abc123def456abc123def456abc123de",
            Type = EiTRVO.ProEngine.Models.AccountType.Yggdrasil,
            YggdrasilServerUrl = "https://auth.example.com",
            YggdrasilEmail = "test@test.com",
            YggdrasilAccessToken = "token-abc",
            YggdrasilClientToken = "client-xyz"
        };

        var vm = CreateYggdrasilTestVm();
        vm.YggdrasilServerUrl = "https://auth.example.com";
        vm.YggdrasilEmail = "test@test.com";
        vm.YggdrasilPassword = "correct-horse-battery-staple";

        await vm.YggdrasilLoginCommand.ExecuteAsync(null);

        // Should not have shown any security warning
        Assert.IsFalse(
            (_notification.LastShowMessage?.Contains("非法字符") == true) ||
            (_notification.LastShowMessage?.Contains("https://") == true) ||
            (_notification.LastShowMessage?.Contains("验证服务器 URL") == true),
            "Security warning was incorrectly shown for valid URL.");
    }
}
