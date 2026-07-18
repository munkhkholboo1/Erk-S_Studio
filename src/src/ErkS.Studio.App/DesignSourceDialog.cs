using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using Microsoft.Win32;

namespace ErkS.Studio;

internal sealed class DesignSourceDialog : Window
{
    private readonly ProjectWorkspace project;
    private readonly Func<string, string> defaultFolderResolver;
    private readonly ComboBox kindBox = new();
    private readonly TextBox nameBox = new();
    private readonly TextBox inboxBox = new();
    private readonly TextBox documentTitleBox = new();
    private readonly TextBox documentPathBox = new();
    private readonly TextBox ownerBox = new();
    private readonly Button browseDocumentButton = StudioWidgets.CreateInlineButton("RVT файл сонгох...");

    public ProjectDesignSource? ResultSource { get; private set; }

    public DesignSourceDialog(ProjectWorkspace project, Func<string, string> defaultFolderResolver)
    {
        this.project = project;
        this.defaultFolderResolver = defaultFolderResolver;
        Title = "Эх үүсвэр нэмэх";
        Width = 620;
        Height = 520;
        MinWidth = 520;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);

        kindBox.ItemsSource = Enum.GetValues<DesignSourceKind>();
        kindBox.SelectedItem = DesignSourceKind.Revit;
        nameBox.Text = "Revit - Архитектур";
        ownerBox.Text = project.DesignOrganizationName;
        inboxBox.Text = defaultFolderResolver(nameBox.Text);
        inboxBox.IsReadOnly = true;
        documentTitleBox.IsReadOnly = true;
        documentPathBox.IsReadOnly = true;
        ownerBox.IsReadOnly = true;

        kindBox.SelectionChanged += (_, _) => ApplyKindDefaults();
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        var add = StudioWidgets.CreatePrimaryButton("Эх үүсвэр нэмэх");
        add.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(add);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var form = new StackPanel();
        form.Children.Add(StudioWidgets.CreateTitle("Эх үүсвэр"));
        form.Children.Add(StudioWidgets.CreateFormRow("Төрөл", kindBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Нэр", nameBox));

        form.Children.Add(StudioWidgets.CreateFormRow("Project inbox", inboxBox));

        form.Children.Add(StudioWidgets.CreateSectionHeader("Холболтын мэдээлэл"));
        form.Children.Add(StudioWidgets.CreateFormRow("Файлын нэр", documentTitleBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Native файл", BuildNativeDocumentPicker()));
        form.Children.Add(StudioWidgets.CreateFormRow("Үе шат", new TextBlock
        {
            Text = project.Identity.StageName,
            VerticalAlignment = VerticalAlignment.Center,
        }));
        form.Children.Add(StudioWidgets.CreateFormRow("Багц", new TextBlock
        {
            Text = "Барилга архитектурын загвар зураг",
            VerticalAlignment = VerticalAlignment.Center,
        }));
        form.Children.Add(StudioWidgets.CreateFormRow("Хариуцагч", ownerBox));
        root.Children.Add(new ScrollViewer
        {
            Content = form,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        return root;
    }

    private void ApplyKindDefaults()
    {
        if (kindBox.SelectedItem is not DesignSourceKind kind)
        {
            return;
        }

        var defaultName = kind switch
        {
            DesignSourceKind.Revit => "Revit - Архитектур",
            DesignSourceKind.AutoCad => "AutoCAD - Layout",
            DesignSourceKind.CityGen => "CityGen - Layout",
            DesignSourceKind.Pdf => "PDF баримт",
            _ => "Хавтас",
        };
        nameBox.Text = defaultName;
        inboxBox.Text = defaultFolderResolver(defaultName);
        documentTitleBox.Clear();
        documentPathBox.Clear();
        browseDocumentButton.Content = kind switch
        {
            DesignSourceKind.Revit => "RVT файл сонгох...",
            DesignSourceKind.AutoCad => "DWG файл сонгох...",
            DesignSourceKind.CityGen => "CityGen файл сонгох...",
            DesignSourceKind.Pdf => "PDF файл сонгох...",
            _ => "Хавтас сонгох...",
        };
    }

    private UIElement BuildNativeDocumentPicker()
    {
        var picker = new Grid();
        picker.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        picker.Children.Add(documentPathBox);
        browseDocumentButton.Margin = new Thickness(6, 0, 0, 0);
        browseDocumentButton.Click += (_, _) => BrowseNativeDocument();
        Grid.SetColumn(browseDocumentButton, 1);
        picker.Children.Add(browseDocumentButton);
        return picker;
    }

    private void BrowseNativeDocument()
    {
        if (kindBox.SelectedItem is not DesignSourceKind kind)
        {
            return;
        }

        if (kind == DesignSourceKind.Folder)
        {
            BrowseNativeFolder();
            return;
        }

        var (title, filter, defaultExtension) = kind switch
        {
            DesignSourceKind.Revit => ("Revit төслийн файл сонгох", "Revit project (*.rvt)|*.rvt|Бүх файл (*.*)|*.*", ".rvt"),
            DesignSourceKind.AutoCad => ("AutoCAD зургийн файл сонгох", "AutoCAD drawing (*.dwg)|*.dwg|Бүх файл (*.*)|*.*", ".dwg"),
            DesignSourceKind.CityGen => ("CityGen эх файл сонгох", "CityGen data (*.json;*.geojson;*.zip)|*.json;*.geojson;*.zip|Бүх файл (*.*)|*.*", ""),
            DesignSourceKind.Pdf => ("PDF файл сонгох", "PDF document (*.pdf)|*.pdf|Бүх файл (*.*)|*.*", ".pdf"),
            _ => ("Эх файл сонгох", "Бүх файл (*.*)|*.*", ""),
        };
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            DereferenceLinks = true,
            InitialDirectory = ResolvePickerDirectory(),
        };

        if (dialog.ShowDialog(this) == true)
        {
            SetNativeDocument(dialog.FileName);
        }
    }

    private void BrowseNativeFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Эх хавтас сонгох",
            InitialDirectory = ResolvePickerDirectory(),
            Multiselect = false,
        };
        if (dialog.ShowDialog(this) == true)
        {
            SetNativeDocument(dialog.FolderName);
        }
    }

    private string ResolvePickerDirectory()
    {
        var currentPath = documentPathBox.Text.Trim();
        if (File.Exists(currentPath))
        {
            return Path.GetDirectoryName(currentPath)
                ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
        if (Directory.Exists(currentPath))
        {
            return currentPath;
        }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void SetNativeDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        documentPathBox.Text = fullPath;
        documentTitleBox.Text = Directory.Exists(fullPath)
            ? new DirectoryInfo(fullPath).Name
            : Path.GetFileName(fullPath);
    }

    private void Accept()
    {
        if (kindBox.SelectedItem is not DesignSourceKind kind)
        {
            return;
        }

        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StudioMessageDialog.Show(this, "Эх үүсвэрийн нэр оруулна уу.", "Erk-S Studio", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var inbox = string.IsNullOrWhiteSpace(inboxBox.Text)
            ? defaultFolderResolver(name)
            : inboxBox.Text.Trim();
        ResultSource = new ProjectDesignSource
        {
            Kind = kind,
            Name = name,
            InboxFolder = inbox,
            NativeDocumentTitle = documentTitleBox.Text.Trim(),
            NativeDocumentPath = documentPathBox.Text.Trim(),
            OwnerOrganizationName = ownerBox.Text.Trim(),
            Status = kind is DesignSourceKind.Pdf or DesignSourceKind.Folder
                ? DesignSourceStatuses.Connected
                : DesignSourceStatuses.WaitingForConnection,
        };
        DialogResult = true;
    }

}
