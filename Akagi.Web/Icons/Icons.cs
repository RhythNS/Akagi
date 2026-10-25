using TabBlazor;

namespace Akagi.Web.Icons;

public static class Icons
{
    // Navigation
    public static IIconType Home => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M5 12l-2 0l9 -9l9 9l-2 0' /><path d='M5 12v7a2 2 0 0 0 2 2h10a2 2 0 0 0 2 -2v-7' /><path d='M9 21v-6a2 2 0 0 1 2 -2h2a2 2 0 0 1 2 2v6' />");

    public static IIconType Messages => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M21 14l-3 -3h-7a1 1 0 0 1 -1 -1v-6a1 1 0 0 1 1 -1h9a1 1 0 0 1 1 1v10' /><path d='M14 15v2a1 1 0 0 1 -1 1h-7l-3 3v-10a1 1 0 0 1 1 -1h2' />");

    public static IIconType Clock => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0' /><path d='M12 7v5l3 3' />");

    public static IIconType Settings => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M10.325 4.317c.426 -1.756 2.924 -1.756 3.35 0a1.724 1.724 0 0 0 2.573 1.066c1.543 -.94 3.31 .826 2.37 2.37a1.724 1.724 0 0 0 1.065 2.572c1.756 .426 1.756 2.924 0 3.35a1.724 1.724 0 0 0 -1.066 2.573c.94 1.543 -.826 3.31 -2.37 2.37a1.724 1.724 0 0 0 -2.572 1.065c-.426 1.756 -2.924 1.756 -3.35 0a1.724 1.724 0 0 0 -2.573 -1.066c-1.543 .94 -3.31 -.826 -2.37 -2.37a1.724 1.724 0 0 0 -1.065 -2.572c-1.756 -.426 -1.756 -2.924 0 -3.35a1.724 1.724 0 0 0 1.066 -2.573c-.94 -1.543 .826 -3.31 2.37 -2.37c1 .608 2.296 .07 2.572 -1.065z' /><path d='M9 12a3 3 0 1 0 6 0a3 3 0 0 0 -6 0' />");

    public static IIconType User => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M8 7a4 4 0 1 0 8 0a4 4 0 0 0 -8 0' /><path d='M6 21v-2a4 4 0 0 1 4 -4h4a4 4 0 0 1 4 4v2' />");

    public static IIconType Logout => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M14 8v-2a2 2 0 0 0 -2 -2h-7a2 2 0 0 0 -2 2v12a2 2 0 0 0 2 2h7a2 2 0 0 0 2 -2v-2' /><path d='M9 12h12l-3 -3' /><path d='M18 15l3 -3' />");

    // Chat Actions
    public static IIconType Send => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M10 14l11 -11' /><path d='M21 3l-6.5 18a.55 .55 0 0 1 -1 0l-3.5 -7l-7 -3.5a.55 .55 0 0 1 0 -1l18 -6.5' />");

    public static IIconType Paperclip => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M15 7l-6.5 6.5a1.5 1.5 0 0 0 3 3l6.5 -6.5a3 3 0 0 0 -6 -6l-6.5 6.5a4.5 4.5 0 0 0 9 9l6.5 -6.5' />");

    public static IIconType DotsVertical => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M12 12m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0' /><path d='M12 19m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0' /><path d='M12 5m-1 0a1 1 0 1 0 2 0a1 1 0 1 0 -2 0' />");

    public static IIconType Sparkles => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2zm0 -12a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2zm-7 12a6 6 0 0 1 6 -6a6 6 0 0 1 -6 -6a6 6 0 0 1 -6 6a6 6 0 0 1 6 6z' />");

    public static IIconType Refresh => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M20 11a8.1 8.1 0 0 0 -15.5 -2m-.5 -4v4h4' /><path d='M4 13a8.1 8.1 0 0 0 15.5 2m.5 4v-4h-4' />");

    public static IIconType ArrowLeft => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M5 12l14 0' /><path d='M5 12l6 6' /><path d='M5 12l6 -6' />");

    public static IIconType Trash => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M4 7l16 0' /><path d='M10 11l0 6' /><path d='M14 11l0 6' /><path d='M5 7l1 12a2 2 0 0 0 2 2h8a2 2 0 0 0 2 -2l1 -12' /><path d='M9 7v-3a1 1 0 0 1 1 -1h4a1 1 0 0 1 1 1v3' />");

    // Status
    public static IIconType Power => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M7 6a7.75 7.75 0 1 0 10 0' /><path d='M12 4l0 8' />");

    public static IIconType CircleFilled => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M7 3.34a10 10 0 1 1 -4.995 8.984l-.005 -.324l.005 -.324a10 10 0 0 1 4.995 -8.336z' stroke-width='0' fill='currentColor' />");

    public static IIconType Sun => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M12 12m-4 0a4 4 0 1 0 8 0a4 4 0 1 0 -8 0' /><path d='M3 12h1m8 -9v1m8 8h1m-9 8v1m-6.4 -15.4l.7 .7m12.1 -.7l-.7 .7m0 11.4l.7 .7m-12.1 -.7l-.7 .7' />");

    public static IIconType Moon => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M12 3c.132 0 .263 0 .393 0a7.5 7.5 0 0 0 7.92 12.446a9 9 0 1 1 -8.313 -12.454z' />");

    public static IIconType Check => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M5 12l5 5l10 -10' />");

    public static IIconType AlertCircle => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M3 12a9 9 0 1 0 18 0a9 9 0 0 0 -18 0' /><path d='M12 8v4' /><path d='M12 16h.01' />");

    // Common Actions & Backward-Compatible Aliases
    public static IIconType Plus => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M12 5l0 14' /><path d='M5 12l14 0' />");

    public static IIconType FileExport => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M14 3v4a1 1 0 0 0 1 1h4' /><path d='M11.5 21h-4.5a2 2 0 0 1 -2 -2v-14a2 2 0 0 1 2 -2h7l5 5v5m-5 6h7m-3 -3l3 3l-3 3' />");

    public static IIconType Edit => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M7 7h-1a2 2 0 0 0 -2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2 -2v-1' /><path d='M20.385 6.585a2.1 2.1 0 0 0 -2.97 -2.97l-8.415 8.385v3h3l8.385 -8.415z' /><path d='M16 5l3 3' />");

    public static IIconType Search => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M10 10m-7 0a7 7 0 1 0 14 0a7 7 0 1 0 -14 0' /><path d='M21 21l-6 -6' />");

    public static IIconType Heart => new TablerIcon(
        @"<path stroke='none' d='M0 0h24v24H0z' fill='none'/><path d='M19.5 12.572l-7.5 7.428l-7.5 -7.428a5 5 0 1 1 7.5 -6.566a5 5 0 1 1 7.5 6.572' />");

    public static IIconType New => Plus;
    public static IIconType Delete => Trash;
    public static IIconType Dots_vertical => DotsVertical;
}
