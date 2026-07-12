using System.Text;
using System.Windows;

namespace SibangGenerator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // EUC-KR / CP949 지원을 위해 반드시 먼저 등록
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
    }
}
