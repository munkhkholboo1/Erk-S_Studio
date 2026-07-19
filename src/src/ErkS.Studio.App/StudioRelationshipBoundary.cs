using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal enum StudioRelationshipAction
{
    InviteProjectMember,
    AcceptProjectMembership,
    RemoveProjectMember,
    UpdateProjectMemberRoles,
    AssignProjectArchitect,
    RequestProjectExit,
    DecideProjectExit,
    IssueProjectCreationGrant,
    RedeemProjectCreationGrant,
    AssignDesignOrganization,
    TransferSourceCustody,
    CreateProjectForClient,
    DeleteProject,
}

internal static class StudioRelationshipBoundary
{
    public const string PolicyVersion = "ERKS-RELATIONSHIP-BOUNDARY-2026-07-17";
    public const string HeaderName = "X-ErkS-Relationship-Boundary";

    public static bool Confirm(
        Window? owner,
        StudioRelationshipAction action,
        string counterparty = "")
    {
        var dialog = new RelationshipBoundaryDialog(action, counterparty)
        {
            Owner = owner,
        };
        return dialog.ShowDialog() == true;
    }

    public static string ActionLabel(StudioRelationshipAction action) => action switch
    {
        StudioRelationshipAction.InviteProjectMember => "Төслийн багийн урилга илгээх",
        StudioRelationshipAction.AcceptProjectMembership => "Төслийн багт нэгдэх",
        StudioRelationshipAction.RemoveProjectMember => "Төслийн гишүүнийг хасах",
        StudioRelationshipAction.UpdateProjectMemberRoles => "Төслийн гишүүний үүрэг, эрхийг өөрчлөх",
        StudioRelationshipAction.AssignProjectArchitect => "Төслийн архитектор томилох",
        StudioRelationshipAction.RequestProjectExit => "Төслөөс гарах хүсэлт илгээх",
        StudioRelationshipAction.DecideProjectExit => "Төслөөс гарах хүсэлтийг шийдвэрлэх",
        StudioRelationshipAction.IssueProjectCreationGrant => "Байгууллагын нэр дээр төсөл үүсгэх эрх олгох",
        StudioRelationshipAction.RedeemProjectCreationGrant => "Олгосон эрхээр төсөл үүсгэх",
        StudioRelationshipAction.AssignDesignOrganization => "Зураг төслийн байгууллага сонгох эсвэл солих",
        StudioRelationshipAction.TransferSourceCustody => "Эх үүсвэрийн хариуцагч шилжүүлэх",
        StudioRelationshipAction.CreateProjectForClient => "Захиалагчтай төсөл үүсгэх",
        StudioRelationshipAction.DeleteProject => "Өөрийн үүсгэсэн төслийг идэвхтэй жагсаалтаас устгах",
        _ => "Талуудын хооронд эрх, үүрэг үүсгэх",
    };

    private sealed class RelationshipBoundaryDialog : Window
    {
        private readonly Button continueButton = StudioWidgets.CreatePrimaryButton("Ойлгож, үргэлжлүүлэх");

        public RelationshipBoundaryDialog(StudioRelationshipAction action, string counterparty)
        {
            Title = "Харилцааны нөхцөл";
            Width = 700;
            Height = 570;
            MinWidth = 620;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            StudioTheme.Apply(this);
            Content = BuildContent(action, counterparty);
        }

        private UIElement BuildContent(StudioRelationshipAction action, string counterparty)
        {
            var root = new DockPanel { Margin = new Thickness(24) };

            var footer = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
            var version = StudioWidgets.CreateHint("Policy: " + PolicyVersion);
            version.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(version);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            var cancelButton = StudioWidgets.CreateButton("Болих");
            cancelButton.IsCancel = true;
            continueButton.IsEnabled = false;
            continueButton.Click += (_, _) => DialogResult = true;
            actions.Children.Add(cancelButton);
            actions.Children.Add(continueButton);
            DockPanel.SetDock(actions, Dock.Right);
            footer.Children.Add(actions);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var content = new StackPanel();
            content.Children.Add(StudioWidgets.CreateTitle("Харилцааны хариуцлагын сануулга"));
            content.Children.Add(StudioWidgets.CreateHint(ActionLabel(action)));
            if (!string.IsNullOrWhiteSpace(counterparty))
            {
                content.Children.Add(new TextBlock
                {
                    Text = "Холбогдох тал: " + counterparty.Trim(),
                    Margin = new Thickness(0, 14, 0, 0),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = StudioTheme.TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            var notice = new Border
            {
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(16),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                Background = StudioTheme.PanelBrush,
            };
            var paragraphs = new StackPanel();
            paragraphs.Children.Add(Paragraph(ActionNotice(action), FontWeights.SemiBold));
            paragraphs.Children.Add(Paragraph(
                "Энэ үйлдэл нь захиалагч, байгууллага эсвэл төслийн гишүүний хооронд эрх, үүрэг үүсгэж болно. " +
                "Erk-S нь тэдгээр талын гэрээ, хөдөлмөр, төлбөр тооцоо болон бусад хуулийн харилцааны тал, " +
                "батлан даагч эсвэл маргаан шийдвэрлэгч биш."));
            paragraphs.Children.Add(Paragraph(
                "Талуудын маргаан, ажлын үр дүн, хугацаа, төлбөр, нууцлал, хүлээлцэх ажиллагааны хариуцлагыг " +
                "платформ хүлээхгүй. Erk-S аль нэг талд давуу байдал бий болгох зорилгоор нөгөө талын нууц, " +
                "дотоод эсвэл зөвшөөрөлгүй мэдээллийг өгөхгүй; зөвхөн project role ба scope-оор зөвшөөрсөн мэдээллийг харуулна."));
            paragraphs.Children.Add(Paragraph(
                "RVT, DWG болон бусад мэргэжлийн эх файл Erk-S-ээр хадгалагдах, upload хийгдэх эсвэл дамжихгүй. " +
                "Эх файлын нөөцлөлт, солилцоо, хүлээлцэх ажиллагаа болон ашиглах эрхийг талууд платформоос гадуур өөрсдөө зохицуулна."));
            paragraphs.Children.Add(Paragraph(
                "Erk-S нь төслийн role, зөвшөөрөл, PDF/тайлан, manifest болон аудитын бүртгэлийг удирдана. " +
                "Энэ баталгаажуулалт нь талуудын хооронд гэрээ байгуулсан гэж тооцогдохгүй. Харин Erk-S өөрийн " +
                "аюулгүй байдал, нууцлал, access control болон аудитын зөв ажиллагааг хариуцсан хэвээр байна."));
            notice.Child = paragraphs;
            content.Children.Add(notice);

            var acknowledgement = new CheckBox
            {
                Margin = new Thickness(0, 18, 0, 0),
                Content = new TextBlock
                {
                    Text = "Дээрх хязгаарлалт, төвийг сахих зарчим болон эх файлын хариуцлагыг уншиж ойлголоо.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = StudioTheme.TextBrush,
                },
            };
            acknowledgement.Checked += (_, _) => continueButton.IsEnabled = true;
            acknowledgement.Unchecked += (_, _) => continueButton.IsEnabled = false;
            content.Children.Add(acknowledgement);

            root.Children.Add(new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            });
            return root;
        }

        private static string ActionNotice(StudioRelationshipAction action) => action switch
        {
            StudioRelationshipAction.InviteProjectMember =>
                "Урилга нь зөвхөн project access ба role санал болгоно. Энэ нь ажилд авсан, үйлчилгээний гэрээ байгуулсан, " +
                "цалин/хөлс, хугацаа, оюуны өмч эсвэл эх файл хүлээлцэх нөхцөл тохирсон гэсэн үг биш.",
            StudioRelationshipAction.AcceptProjectMembership =>
                "Зөвшөөрснөөр таны бүртгэл төслийн role-той холбогдоно. Энэ нь ажил, төлбөр, зохиогчийн эрх, нууцлал, " +
                "хүлээлцэх нөхцөлийг Erk-S баталгаажуулсан гэсэн үг биш; тэдгээрийг урьсан талтай тусад нь тохиролцоно.",
            StudioRelationshipAction.RemoveProjectMember =>
                "Хассанаар платформын access хаагдаж, хариуцаж байсан cloud source Unassigned болно. PDF, manifest ба аудитын " +
                "түүх үлдэнэ. Энэ үйлдэл төлбөр, оюуны өмч, ажлын үр дүн эсвэл эх файл хүлээлцсэн маргааныг шийдвэрлэхгүй.",
            StudioRelationshipAction.UpdateProjectMemberRoles =>
                "Role өөрчилснөөр тухайн гишүүний төсөл харах, эх үүсвэр боловсруулах, альбум илгээх эсвэл баг удирдах техникийн эрх " +
                "шууд өөрчлөгдөнө. Энэ нь ажлын байр, гэрээ, төлбөр, мэргэжлийн хариуцлага эсвэл эх файл хүлээлцсэн баримт биш. " +
                "Major architect role-г өөр хүнд өгөхөд төслийн үндсэн архитекторын томилгоо мөн шилжинэ.",
            StudioRelationshipAction.AssignProjectArchitect =>
                "Томилсноор бүртгэлтэй оролцогч төслийн үндсэн архитекторын role авч, түүний profile нэр альбумын булангийн " +
                "хүснэгтэд ашиглагдана. Erk-S нь тухайн хүний мэргэжлийн эрх, хөдөлмөрийн харилцаа, байгууллагыг төлөөлөх бүрэн эрх " +
                "эсвэл хуулийн хариуцлагыг баталгаажуулахгүй; томилогч тал эдгээр үндэслэлийг өөрөө хариуцна.",
            StudioRelationshipAction.RequestProjectExit =>
                "Гарах хүсэлт батлагдах хүртэл access хэвээр байна. Хүсэлт нь гэрээ цуцалсан, тооцоо нийлсэн, ажлын үр дүн " +
                "болон эх файл хүлээлцсэн баримт биш.",
            StudioRelationshipAction.DecideProjectExit =>
                "Зөвшөөрвөл project access хаагдаж, хариуцаж байсан cloud source Unassigned болно; татгалзвал access хэвээр үлдэнэ. " +
                "Аль ч шийдвэр нь талуудын төлбөр, оюуны өмч, нууцлал, эх файл хүлээлцэх үүргийг платформ шийдвэрлэсэн гэсэн үг биш.",
            StudioRelationshipAction.IssueProjectCreationGrant =>
                "Энэ нь байгууллагын нэр дээр нэг төсөл үүсгэх техникийн эрх. Компанийн өмчлөл, тусгай зөвшөөрөл, итгэмжлэл, " +
                "ажилд авах эсвэл төлбөрийн эрх шилжүүлэхгүй. Эрх олгогч байгууллага хүлээн авагчтайгаа тусдаа тохиролцоно.",
            StudioRelationshipAction.RedeemProjectCreationGrant =>
                "Эрхийг ашигласнаар төсөл байгууллагын snapshot-тай холбогдоно. Энэ нь байгууллагатай гэрээ байгуулсан, түүнийг " +
                "төлөөлөх бүрэн эрхтэй эсвэл захиалагчийн зөвшөөрөл авсан гэдгийг Erk-S баталсан гэсэн үг биш.",
            StudioRelationshipAction.AssignDesignOrganization =>
                "Захиалагч зураг төслийн байгууллагыг project record-д сонгож эсвэл солино. Энэ сонголт нь худалдан авалт, " +
                "гэрээ, төлбөр, тусгай зөвшөөрлийн хүчинтэй байдал эсвэл ажлын чанарыг Erk-S баталгаажуулсан гэсэн үг биш.",
            StudioRelationshipAction.TransferSourceCustody =>
                "Энэ үйлдэл cloud source-ийн хариуцагчийг л солино. RVT/DWG файл, зохиогчийн эрх, ашиглах лиценз, төлбөр, " +
                "ажлын үр дүн хүлээлцсэн гэж тооцохгүй; локал файлыг шинэ хариуцагч өөрийн төхөөрөмж дээр дахин холбоно.",
            StudioRelationshipAction.CreateProjectForClient =>
                "Захиалагчийн нэр, мэдээлэл оруулах нь захиалагч зөвшөөрсөн, гэрээ байгуулсан, төлөөлөх эрх олгосон эсвэл " +
                "оруулсан мэдээлэл үнэн зөв гэдгийг Erk-S баталгаажуулсан гэсэн үг биш. Мэдээлэл оруулагч хууль ёсны үндэслэлээ хариуцна.",
            StudioRelationshipAction.DeleteProject =>
                "Устгаснаар төсөл бүх оролцогчийн идэвхтэй Cloud жагсаалтаас хасагдана. Canonical мэдээлэл, approval болон аудитын түүх " +
                "маргаан, алдаа эсвэл сэргээх шаардлагад зориулан серверт хадгалагдана. Локал RVT/DWG, mirror болон PDF файлууд устахгүй. " +
                "Энэ үйлдэл захиалагч болон бусад талтай байгуулсан гэрээ, төлбөр, оюуны өмчийн харилцааг цуцлахгүй.",
            _ =>
                "Энэ нь зөвхөн платформ дахь техникийн эрх ба мэдээллийн холбоосыг өөрчилнө; талуудын хуулийн харилцааг үүсгэхгүй, батлахгүй.",
        };

        private static TextBlock Paragraph(string text, FontWeight? weight = null) => new()
        {
            Text = text,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
            Foreground = StudioTheme.TextBrush,
            LineHeight = 20,
            FontWeight = weight ?? FontWeights.Normal,
        };
    }
}
