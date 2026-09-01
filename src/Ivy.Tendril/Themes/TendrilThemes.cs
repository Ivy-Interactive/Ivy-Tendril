namespace Ivy.Tendril.Themes;

public class TendrilThemeDescriptor
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public bool IsDark { get; init; }
    public string[] PreviewColors { get; init; } = [];
    public Theme IvyTheme { get; init; } = Theme.Default;
}

public static class TendrilThemes
{
    public static readonly TendrilThemeDescriptor Default = new()
    {
        Id = "default",
        Name = "Default",
        Description = "Standard clean Tendril theme with slate light and dark zinc",
        IsDark = false,
        PreviewColors = ["#18181b", "#71717a", "#27272a", "#ffffff"],
        IvyTheme = Theme.Default
    };

    public static readonly TendrilThemeDescriptor Cupcake = new()
    {
        Id = "cupcake",
        Name = "Cupcake",
        Description = "Warm cream background with soft teal, pastel pink, and amber accents",
        IsDark = false,
        PreviewColors = ["#65c3c8", "#ef9fbc", "#eeaf3a", "#faf7f5"],
        IvyTheme = new Theme
        {
            Name = "Cupcake",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#65c3c8",
                    PrimaryForeground = "#1d232a",
                    Secondary = "#ef9fbc",
                    SecondaryForeground = "#1d232a",
                    Accent = "#eeaf3a",
                    AccentForeground = "#1d232a",
                    Background = "#faf7f5",
                    Foreground = "#291334",
                    Destructive = "#f87272",
                    DestructiveForeground = "#ffffff",
                    Success = "#36d399",
                    SuccessForeground = "#1d232a",
                    Warning = "#fbbd23",
                    WarningForeground = "#1d232a",
                    Info = "#3abff8",
                    InfoForeground = "#1d232a",
                    Border = "#e7e2df",
                    Input = "#f3eeea",
                    Ring = "#65c3c8",
                    Muted = "#eae5e1",
                    MutedForeground = "#5e5264",
                    Card = "#ffffff",
                    CardForeground = "#291334",
                    Popover = "#faf7f5",
                    PopoverForeground = "#291334"
                },
                Dark = new ThemeColors
                {
                    Primary = "#65c3c8",
                    PrimaryForeground = "#1d232a",
                    Secondary = "#ef9fbc",
                    SecondaryForeground = "#1d232a",
                    Accent = "#eeaf3a",
                    AccentForeground = "#1d232a",
                    Background = "#231a28",
                    Foreground = "#faf7f5",
                    Destructive = "#f87272",
                    DestructiveForeground = "#ffffff",
                    Success = "#36d399",
                    SuccessForeground = "#1d232a",
                    Warning = "#fbbd23",
                    WarningForeground = "#1d232a",
                    Info = "#3abff8",
                    InfoForeground = "#1d232a",
                    Border = "#44344e",
                    Input = "#2f2436",
                    Ring = "#65c3c8",
                    Muted = "#3a2d43",
                    MutedForeground = "#bbaabf",
                    Card = "#2f2436",
                    CardForeground = "#faf7f5",
                    Popover = "#231a28",
                    PopoverForeground = "#faf7f5"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Cyberpunk = new()
    {
        Id = "cyberpunk",
        Name = "Cyberpunk",
        Description = "High-contrast neon yellow, vivid pink, and cyber cyan",
        IsDark = true,
        PreviewColors = ["#ffee00", "#ff7598", "#2dd4bf", "#111111"],
        IvyTheme = new Theme
        {
            Name = "Cyberpunk",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = "0px",
            BorderRadiusFields = "0px",
            BorderRadiusSelectors = "0px",
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#d4b800",
                    PrimaryForeground = "#000000",
                    Secondary = "#ff7598",
                    SecondaryForeground = "#000000",
                    Accent = "#0d9488",
                    AccentForeground = "#ffffff",
                    Background = "#ffffeb",
                    Foreground = "#1a1a1a",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#0d9488",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#e5df8a",
                    Input = "#fbf6c7",
                    Ring = "#d4b800",
                    Muted = "#f3eed0",
                    MutedForeground = "#5e5840",
                    Card = "#ffffff",
                    CardForeground = "#1a1a1a",
                    Popover = "#ffffeb",
                    PopoverForeground = "#1a1a1a"
                },
                Dark = new ThemeColors
                {
                    Primary = "#ffee00",
                    PrimaryForeground = "#000000",
                    Secondary = "#ff7598",
                    SecondaryForeground = "#000000",
                    Accent = "#2dd4bf",
                    AccentForeground = "#000000",
                    Background = "#111111",
                    Foreground = "#ffee00",
                    Destructive = "#ff5555",
                    DestructiveForeground = "#000000",
                    Success = "#2dd4bf",
                    SuccessForeground = "#000000",
                    Warning = "#ffaa00",
                    WarningForeground = "#000000",
                    Info = "#00ddff",
                    InfoForeground = "#000000",
                    Border = "#ffee00",
                    Input = "#222222",
                    Ring = "#ffee00",
                    Muted = "#222218",
                    MutedForeground = "#b3aa00",
                    Card = "#1a1a1a",
                    CardForeground = "#ffee00",
                    Popover = "#111111",
                    PopoverForeground = "#ffee00"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Synthwave = new()
    {
        Id = "synthwave",
        Name = "Synthwave",
        Description = "80s retro neon purple background with hot pink and electric cyan",
        IsDark = true,
        PreviewColors = ["#e779c1", "#58c7f3", "#f3cc30", "#1a103c"],
        IvyTheme = new Theme
        {
            Name = "Synthwave",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#d946ef",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#0284c7",
                    SecondaryForeground = "#ffffff",
                    Accent = "#eab308",
                    AccentForeground = "#000000",
                    Background = "#faf5ff",
                    Foreground = "#1e1035",
                    Destructive = "#ef4444",
                    DestructiveForeground = "#ffffff",
                    Success = "#10b981",
                    SuccessForeground = "#ffffff",
                    Warning = "#f59e0b",
                    WarningForeground = "#ffffff",
                    Info = "#06b6d4",
                    InfoForeground = "#ffffff",
                    Border = "#e9d5ff",
                    Input = "#f3e8ff",
                    Ring = "#d946ef",
                    Muted = "#ede4f8",
                    MutedForeground = "#665482",
                    Card = "#ffffff",
                    CardForeground = "#1e1035",
                    Popover = "#faf5ff",
                    PopoverForeground = "#1e1035"
                },
                Dark = new ThemeColors
                {
                    Primary = "#e779c1",
                    PrimaryForeground = "#1a103c",
                    Secondary = "#58c7f3",
                    SecondaryForeground = "#1a103c",
                    Accent = "#f3cc30",
                    AccentForeground = "#1a103c",
                    Background = "#1a103c",
                    Foreground = "#f3e8ff",
                    Destructive = "#ff5757",
                    DestructiveForeground = "#ffffff",
                    Success = "#2dd4bf",
                    SuccessForeground = "#1a103c",
                    Warning = "#f3cc30",
                    WarningForeground = "#1a103c",
                    Info = "#58c7f3",
                    InfoForeground = "#1a103c",
                    Border = "#3b266e",
                    Input = "#291b54",
                    Ring = "#e779c1",
                    Muted = "#2d1c5b",
                    MutedForeground = "#b8a5e0",
                    Card = "#24184c",
                    CardForeground = "#f3e8ff",
                    Popover = "#1a103c",
                    PopoverForeground = "#f3e8ff"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Retro = new()
    {
        Id = "retro",
        Name = "Retro",
        Description = "Nostalgic warm cream, terracotta primary, sage secondary, and warm gold",
        IsDark = false,
        PreviewColors = ["#ef9995", "#a4cbb4", "#ebdc99", "#ece3ca"],
        IvyTheme = new Theme
        {
            Name = "Retro",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = "0.5rem",
            BorderRadiusFields = "0.5rem",
            BorderRadiusSelectors = "0.5rem",
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#ef9995",
                    PrimaryForeground = "#282425",
                    Secondary = "#a4cbb4",
                    SecondaryForeground = "#282425",
                    Accent = "#ebdc99",
                    AccentForeground = "#282425",
                    Background = "#ece3ca",
                    Foreground = "#282425",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#65a30d",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#d5c79e",
                    Input = "#ded1a9",
                    Ring = "#ef9995",
                    Muted = "#dcd0aa",
                    MutedForeground = "#574e44",
                    Card = "#e4d8b4",
                    CardForeground = "#282425",
                    Popover = "#ece3ca",
                    PopoverForeground = "#282425"
                },
                Dark = new ThemeColors
                {
                    Primary = "#ef9995",
                    PrimaryForeground = "#282425",
                    Secondary = "#a4cbb4",
                    SecondaryForeground = "#282425",
                    Accent = "#ebdc99",
                    AccentForeground = "#282425",
                    Background = "#25211e",
                    Foreground = "#ece3ca",
                    Destructive = "#f87171",
                    DestructiveForeground = "#1a1a1a",
                    Success = "#a3e635",
                    SuccessForeground = "#1a1a1a",
                    Warning = "#fbbf24",
                    WarningForeground = "#1a1a1a",
                    Info = "#38bdf8",
                    InfoForeground = "#1a1a1a",
                    Border = "#453e39",
                    Input = "#312c28",
                    Ring = "#ef9995",
                    Muted = "#3a3430",
                    MutedForeground = "#bfae99",
                    Card = "#312c28",
                    CardForeground = "#ece3ca",
                    Popover = "#25211e",
                    PopoverForeground = "#ece3ca"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Dracula = new()
    {
        Id = "dracula",
        Name = "Dracula",
        Description = "Classic dark violet background with purple, pink, and cyan accents",
        IsDark = true,
        PreviewColors = ["#bd93f9", "#ff79c6", "#44475a", "#282a36"],
        IvyTheme = new Theme
        {
            Name = "Dracula",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#7c3aed",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#db2777",
                    SecondaryForeground = "#ffffff",
                    Accent = "#0891b2",
                    AccentForeground = "#ffffff",
                    Background = "#f8f7fc",
                    Foreground = "#282a36",
                    Destructive = "#e11d48",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#ca8a04",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#e2e0ed",
                    Input = "#f0eef7",
                    Ring = "#7c3aed",
                    Muted = "#eae8f3",
                    MutedForeground = "#525770",
                    Card = "#ffffff",
                    CardForeground = "#282a36",
                    Popover = "#f8f7fc",
                    PopoverForeground = "#282a36"
                },
                Dark = new ThemeColors
                {
                    Primary = "#bd93f9",
                    PrimaryForeground = "#282a36",
                    Secondary = "#ff79c6",
                    SecondaryForeground = "#282a36",
                    Accent = "#44475a",
                    AccentForeground = "#f8f8f2",
                    Background = "#282a36",
                    Foreground = "#f8f8f2",
                    Destructive = "#ff5555",
                    DestructiveForeground = "#f8f8f2",
                    Success = "#50fa7b",
                    SuccessForeground = "#282a36",
                    Warning = "#f1fa8c",
                    WarningForeground = "#282a36",
                    Info = "#8be9fd",
                    InfoForeground = "#282a36",
                    Border = "#44475a",
                    Input = "#383a59",
                    Ring = "#bd93f9",
                    Muted = "#34374a",
                    MutedForeground = "#b0b7da",
                    Card = "#343746",
                    CardForeground = "#f8f8f2",
                    Popover = "#282a36",
                    PopoverForeground = "#f8f8f2"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Nord = new()
    {
        Id = "nord",
        Name = "Nord",
        Description = "Arctic cool palette with slate background, frost blue, and polar night darks",
        IsDark = true,
        PreviewColors = ["#88c0d0", "#81a1c1", "#5e81ac", "#2e3440"],
        IvyTheme = new Theme
        {
            Name = "Nord",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#5e81ac",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#81a1c1",
                    SecondaryForeground = "#2e3440",
                    Accent = "#88c0d0",
                    AccentForeground = "#2e3440",
                    Background = "#eceff4",
                    Foreground = "#2e3440",
                    Destructive = "#bf616a",
                    DestructiveForeground = "#ffffff",
                    Success = "#a3be8c",
                    SuccessForeground = "#2e3440",
                    Warning = "#ebcb8b",
                    WarningForeground = "#2e3440",
                    Info = "#88c0d0",
                    InfoForeground = "#2e3440",
                    Border = "#d8dee9",
                    Input = "#e5e9f0",
                    Ring = "#5e81ac",
                    Muted = "#d8dee9",
                    MutedForeground = "#4c566a",
                    Card = "#e5e9f0",
                    CardForeground = "#2e3440",
                    Popover = "#eceff4",
                    PopoverForeground = "#2e3440"
                },
                Dark = new ThemeColors
                {
                    Primary = "#88c0d0",
                    PrimaryForeground = "#2e3440",
                    Secondary = "#81a1c1",
                    SecondaryForeground = "#2e3440",
                    Accent = "#5e81ac",
                    AccentForeground = "#eceff4",
                    Background = "#2e3440",
                    Foreground = "#eceff4",
                    Destructive = "#bf616a",
                    DestructiveForeground = "#eceff4",
                    Success = "#a3be8c",
                    SuccessForeground = "#2e3440",
                    Warning = "#ebcb8b",
                    WarningForeground = "#2e3440",
                    Info = "#88c0d0",
                    InfoForeground = "#2e3440",
                    Border = "#4c566a",
                    Input = "#3b4252",
                    Ring = "#88c0d0",
                    Muted = "#434c5e",
                    MutedForeground = "#b4c2d6",
                    Card = "#3b4252",
                    CardForeground = "#eceff4",
                    Popover = "#2e3440",
                    PopoverForeground = "#eceff4"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Forest = new()
    {
        Id = "forest",
        Name = "Forest",
        Description = "Deep earthy woodland dark background with fresh emerald and forest green",
        IsDark = true,
        PreviewColors = ["#1eb854", "#1fd65f", "#1db954", "#171212"],
        IvyTheme = new Theme
        {
            Name = "Forest",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#15803d",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#16a34a",
                    SecondaryForeground = "#ffffff",
                    Accent = "#86efac",
                    AccentForeground = "#14532d",
                    Background = "#f0fdf4",
                    Foreground = "#14532d",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#bbf7d0",
                    Input = "#dcfce7",
                    Ring = "#15803d",
                    Muted = "#dcfce7",
                    MutedForeground = "#2d6a4f",
                    Card = "#ffffff",
                    CardForeground = "#14532d",
                    Popover = "#f0fdf4",
                    PopoverForeground = "#14532d"
                },
                Dark = new ThemeColors
                {
                    Primary = "#1eb854",
                    PrimaryForeground = "#000000",
                    Secondary = "#1fd65f",
                    SecondaryForeground = "#000000",
                    Accent = "#1db954",
                    AccentForeground = "#000000",
                    Background = "#171212",
                    Foreground = "#ebfaef",
                    Destructive = "#e11d48",
                    DestructiveForeground = "#ffffff",
                    Success = "#1eb854",
                    SuccessForeground = "#000000",
                    Warning = "#f59e0b",
                    WarningForeground = "#000000",
                    Info = "#06b6d4",
                    InfoForeground = "#000000",
                    Border = "#2f2727",
                    Input = "#231c1c",
                    Ring = "#1eb854",
                    Muted = "#282020",
                    MutedForeground = "#9ab59f",
                    Card = "#1f1919",
                    CardForeground = "#ebfaef",
                    Popover = "#171212",
                    PopoverForeground = "#ebfaef"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Aqua = new()
    {
        Id = "aqua",
        Name = "Aqua",
        Description = "Deep marine ocean background with electric cyan and sky blue accents",
        IsDark = true,
        PreviewColors = ["#09ecf3", "#70a6ff", "#134074", "#0b2545"],
        IvyTheme = new Theme
        {
            Name = "Aqua",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#0284c7",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#38bdf8",
                    SecondaryForeground = "#0b2545",
                    Accent = "#bae6fd",
                    AccentForeground = "#0369a1",
                    Background = "#f0f9ff",
                    Foreground = "#0c4a6e",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#bae6fd",
                    Input = "#e0f2fe",
                    Ring = "#0284c7",
                    Muted = "#e0f2fe",
                    MutedForeground = "#1e6080",
                    Card = "#ffffff",
                    CardForeground = "#0c4a6e",
                    Popover = "#f0f9ff",
                    PopoverForeground = "#0c4a6e"
                },
                Dark = new ThemeColors
                {
                    Primary = "#09ecf3",
                    PrimaryForeground = "#0b2545",
                    Secondary = "#70a6ff",
                    SecondaryForeground = "#0b2545",
                    Accent = "#09ecf3",
                    AccentForeground = "#0b2545",
                    Background = "#0b2545",
                    Foreground = "#eef4f8",
                    Destructive = "#ff5757",
                    DestructiveForeground = "#ffffff",
                    Success = "#00e676",
                    SuccessForeground = "#0b2545",
                    Warning = "#ffeb3b",
                    WarningForeground = "#0b2545",
                    Info = "#09ecf3",
                    InfoForeground = "#0b2545",
                    Border = "#134074",
                    Input = "#113866",
                    Ring = "#09ecf3",
                    Muted = "#133860",
                    MutedForeground = "#9ec3e6",
                    Card = "#13315c",
                    CardForeground = "#eef4f8",
                    Popover = "#0b2545",
                    PopoverForeground = "#eef4f8"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Valentine = new()
    {
        Id = "valentine",
        Name = "Valentine",
        Description = "Romantic blush theme with soft pink background, rose pink, and lavender",
        IsDark = false,
        PreviewColors = ["#e96d7b", "#a991f7", "#88dbdd", "#f0d6e8"],
        IvyTheme = new Theme
        {
            Name = "Valentine",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = "1rem",
            BorderRadiusFields = "1rem",
            BorderRadiusSelectors = "1rem",
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#e96d7b",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#a991f7",
                    SecondaryForeground = "#ffffff",
                    Accent = "#88dbdd",
                    AccentForeground = "#291e25",
                    Background = "#f0d6e8",
                    Foreground = "#632c3b",
                    Destructive = "#e11d48",
                    DestructiveForeground = "#ffffff",
                    Success = "#10b981",
                    SuccessForeground = "#ffffff",
                    Warning = "#f59e0b",
                    WarningForeground = "#ffffff",
                    Info = "#06b6d4",
                    InfoForeground = "#ffffff",
                    Border = "#deb0cf",
                    Input = "#ebd0e2",
                    Ring = "#e96d7b",
                    Muted = "#e4c0d7",
                    MutedForeground = "#6f3546",
                    Card = "#e8c4dc",
                    CardForeground = "#632c3b",
                    Popover = "#f0d6e8",
                    PopoverForeground = "#632c3b"
                },
                Dark = new ThemeColors
                {
                    Primary = "#f472b6",
                    PrimaryForeground = "#371b26",
                    Secondary = "#c084fc",
                    SecondaryForeground = "#371b26",
                    Accent = "#67e8f9",
                    AccentForeground = "#371b26",
                    Background = "#2a1523",
                    Foreground = "#fce7f3",
                    Destructive = "#fb7185",
                    DestructiveForeground = "#1f1218",
                    Success = "#34d399",
                    SuccessForeground = "#1f1218",
                    Warning = "#fbbf24",
                    WarningForeground = "#1f1218",
                    Info = "#38bdf8",
                    InfoForeground = "#1f1218",
                    Border = "#4f2642",
                    Input = "#381c2f",
                    Ring = "#f472b6",
                    Muted = "#422037",
                    MutedForeground = "#d49bbd",
                    Card = "#381c2f",
                    CardForeground = "#fce7f3",
                    Popover = "#2a1523",
                    PopoverForeground = "#fce7f3"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Sunset = new()
    {
        Id = "sunset",
        Name = "Sunset",
        Description = "Dusk gradient dark background with coral orange, sunset rose, and amber",
        IsDark = true,
        PreviewColors = ["#ff865b", "#fd6f9c", "#f3a683", "#121c2a"],
        IvyTheme = new Theme
        {
            Name = "Sunset",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#f97316",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#f43f5e",
                    SecondaryForeground = "#ffffff",
                    Accent = "#fb923c",
                    AccentForeground = "#ffffff",
                    Background = "#fff7ed",
                    Foreground = "#431407",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#fed7aa",
                    Input = "#ffedd5",
                    Ring = "#f97316",
                    Muted = "#ffedd5",
                    MutedForeground = "#803c1d",
                    Card = "#ffffff",
                    CardForeground = "#431407",
                    Popover = "#fff7ed",
                    PopoverForeground = "#431407"
                },
                Dark = new ThemeColors
                {
                    Primary = "#ff865b",
                    PrimaryForeground = "#121c2a",
                    Secondary = "#fd6f9c",
                    SecondaryForeground = "#121c2a",
                    Accent = "#f3a683",
                    AccentForeground = "#121c2a",
                    Background = "#121c2a",
                    Foreground = "#f8e9e2",
                    Destructive = "#ff5757",
                    DestructiveForeground = "#ffffff",
                    Success = "#2dd4bf",
                    SuccessForeground = "#121c2a",
                    Warning = "#fcd34d",
                    WarningForeground = "#121c2a",
                    Info = "#60a5fa",
                    InfoForeground = "#121c2a",
                    Border = "#273a54",
                    Input = "#1e2e44",
                    Ring = "#ff865b",
                    Muted = "#22344c",
                    MutedForeground = "#9fb3cb",
                    Card = "#1b293d",
                    CardForeground = "#f8e9e2",
                    Popover = "#121c2a",
                    PopoverForeground = "#f8e9e2"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Coffee = new()
    {
        Id = "coffee",
        Name = "Coffee",
        Description = "Rich espresso dark background with caramel and warm roasted accents",
        IsDark = true,
        PreviewColors = ["#db924b", "#dc944c", "#94633b", "#20161f"],
        IvyTheme = new Theme
        {
            Name = "Coffee",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#b45309",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#d97706",
                    SecondaryForeground = "#ffffff",
                    Accent = "#fef3c7",
                    AccentForeground = "#78350f",
                    Background = "#fdfbf7",
                    Foreground = "#451a03",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#e7dfd5",
                    Input = "#f5eee6",
                    Ring = "#b45309",
                    Muted = "#f5eee6",
                    MutedForeground = "#78411b",
                    Card = "#ffffff",
                    CardForeground = "#451a03",
                    Popover = "#fdfbf7",
                    PopoverForeground = "#451a03"
                },
                Dark = new ThemeColors
                {
                    Primary = "#db924b",
                    PrimaryForeground = "#20161f",
                    Secondary = "#dc944c",
                    SecondaryForeground = "#20161f",
                    Accent = "#db924b",
                    AccentForeground = "#20161f",
                    Background = "#20161f",
                    Foreground = "#ddd0b9",
                    Destructive = "#ef4444",
                    DestructiveForeground = "#ffffff",
                    Success = "#22c55e",
                    SuccessForeground = "#20161f",
                    Warning = "#eab308",
                    WarningForeground = "#20161f",
                    Info = "#38bdf8",
                    InfoForeground = "#20161f",
                    Border = "#3f2d3d",
                    Input = "#2d202b",
                    Ring = "#db924b",
                    Muted = "#342632",
                    MutedForeground = "#b39f90",
                    Card = "#2a1e29",
                    CardForeground = "#ddd0b9",
                    Popover = "#20161f",
                    PopoverForeground = "#ddd0b9"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Dim = new()
    {
        Id = "dim",
        Name = "Dim",
        Description = "Modern muted charcoal dark background with mint green and slate blue",
        IsDark = true,
        PreviewColors = ["#9fe88d", "#79a7d3", "#ff7ac6", "#2a303c"],
        IvyTheme = new Theme
        {
            Name = "Dim",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#16a34a",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#2563eb",
                    SecondaryForeground = "#ffffff",
                    Accent = "#db2777",
                    AccentForeground = "#ffffff",
                    Background = "#f8fafc",
                    Foreground = "#1e293b",
                    Destructive = "#ef4444",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#f59e0b",
                    WarningForeground = "#ffffff",
                    Info = "#2563eb",
                    InfoForeground = "#ffffff",
                    Border = "#e2e8f0",
                    Input = "#f1f5f9",
                    Ring = "#16a34a",
                    Muted = "#f1f5f9",
                    MutedForeground = "#505f79",
                    Card = "#ffffff",
                    CardForeground = "#1e293b",
                    Popover = "#f8fafc",
                    PopoverForeground = "#1e293b"
                },
                Dark = new ThemeColors
                {
                    Primary = "#9fe88d",
                    PrimaryForeground = "#193614",
                    Secondary = "#79a7d3",
                    SecondaryForeground = "#16283b",
                    Accent = "#ff7ac6",
                    AccentForeground = "#3a1329",
                    Background = "#2a303c",
                    Foreground = "#e2e8f0",
                    Destructive = "#ff6e6e",
                    DestructiveForeground = "#2a303c",
                    Success = "#9fe88d",
                    SuccessForeground = "#193614",
                    Warning = "#f3cc30",
                    WarningForeground = "#2a303c",
                    Info = "#79a7d3",
                    InfoForeground = "#16283b",
                    Border = "#383f4d",
                    Input = "#222731",
                    Ring = "#9fe88d",
                    Muted = "#313846",
                    MutedForeground = "#94a3b8",
                    Card = "#242933",
                    CardForeground = "#e2e8f0",
                    Popover = "#2a303c",
                    PopoverForeground = "#e2e8f0"
                }
            }
        }
    };

    public static readonly TendrilThemeDescriptor Luxury = new()
    {
        Id = "luxury",
        Name = "Luxury",
        Description = "Premium dark gold theme with deep obsidian background and champagne accents",
        IsDark = true,
        PreviewColors = ["#dca54c", "#e0d8b0", "#ff7598", "#09090b"],
        IvyTheme = new Theme
        {
            Name = "Luxury",
            FontFamily = "Geist",
            FontSize = "16px",
            BorderRadiusBoxes = Theme.Default.BorderRadiusBoxes,
            BorderRadiusFields = Theme.Default.BorderRadiusFields,
            BorderRadiusSelectors = Theme.Default.BorderRadiusSelectors,
            Colors = new ThemeColorScheme
            {
                Light = new ThemeColors
                {
                    Primary = "#b45309",
                    PrimaryForeground = "#ffffff",
                    Secondary = "#78350f",
                    SecondaryForeground = "#ffffff",
                    Accent = "#fef3c7",
                    AccentForeground = "#78350f",
                    Background = "#fafaf9",
                    Foreground = "#1c1917",
                    Destructive = "#dc2626",
                    DestructiveForeground = "#ffffff",
                    Success = "#16a34a",
                    SuccessForeground = "#ffffff",
                    Warning = "#d97706",
                    WarningForeground = "#ffffff",
                    Info = "#0284c7",
                    InfoForeground = "#ffffff",
                    Border = "#e7e5e4",
                    Input = "#f5f5f4",
                    Ring = "#b45309",
                    Muted = "#f5f5f4",
                    MutedForeground = "#5f5953",
                    Card = "#ffffff",
                    CardForeground = "#1c1917",
                    Popover = "#fafaf9",
                    PopoverForeground = "#1c1917"
                },
                Dark = new ThemeColors
                {
                    Primary = "#dca54c",
                    PrimaryForeground = "#150e04",
                    Secondary = "#e0d8b0",
                    SecondaryForeground = "#150e04",
                    Accent = "#c28833",
                    AccentForeground = "#ffffff",
                    Background = "#09090b",
                    Foreground = "#f5f5f5",
                    Destructive = "#ef4444",
                    DestructiveForeground = "#ffffff",
                    Success = "#22c55e",
                    SuccessForeground = "#09090b",
                    Warning = "#dca54c",
                    WarningForeground = "#09090b",
                    Info = "#38bdf8",
                    InfoForeground = "#09090b",
                    Border = "#27272a",
                    Input = "#1c1c21",
                    Ring = "#dca54c",
                    Muted = "#202026",
                    MutedForeground = "#a1a1aa",
                    Card = "#141418",
                    CardForeground = "#f5f5f5",
                    Popover = "#09090b",
                    PopoverForeground = "#f5f5f5"
                }
            }
        }
    };

    public static readonly IReadOnlyList<TendrilThemeDescriptor> All =
    [
        Default,
        Cupcake,
        Cyberpunk,
        Synthwave,
        Retro,
        Dracula,
        Nord,
        Forest,
        Aqua,
        Valentine,
        Sunset,
        Coffee,
        Dim,
        Luxury
    ];

    private static readonly Dictionary<string, TendrilThemeDescriptor> ThemesById =
        All.ToDictionary(t => t.Id, t => t, StringComparer.OrdinalIgnoreCase);

    public static TendrilThemeDescriptor GetTheme(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && ThemesById.TryGetValue(id.Trim(), out var theme))
            return theme;
        return Default;
    }

    public static void ApplyTheme(IClientProvider client, string? themeId)
    {
        var descriptor = GetTheme(themeId);
        var themeService = new ThemeService();
        themeService.SetTheme(descriptor.IvyTheme);
        var css = themeService.GenerateThemeCss();
        client.ApplyTheme(css);
    }
}
