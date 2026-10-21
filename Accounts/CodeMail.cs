using System.Net;

namespace OpenKogama.Accounts;

public sealed record CodeMail(string Subject, string Heading, string Intro, string Code, int Minutes, string Footer)
{
    const string Credit = "OpenKogama is a fan-made revival of KoGaMa, not affiliated with Multiverse ApS.";
    const string Font = "'Segoe UI', Roboto, Helvetica, Arial, sans-serif";
    const string Logo = "https://openkogama.org/img/icon-192.png";

    public string Text => $"{Heading}\n\n{Intro}\n\n    {Code}\n\nIt expires in {Minutes} minutes.\n\n{Footer}\n\n{Credit}\n";

    public string Html => $"""
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{E(Subject)}</title></head>
        <body style="margin:0;padding:0;background:#f2f2f2;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f2f2f2;">
        <tr><td align="center" style="padding:32px 16px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:480px;background:#ffffff;border:1px solid #dedede;">
        <tr><td style="padding:28px 32px 0;font-family:{Font};">
        <img src="{Logo}" width="32" height="32" alt="" style="vertical-align:middle;border:0;"><span style="padding-left:8px;font-size:17px;color:#1e3460;vertical-align:middle;">OpenKogama</span>
        </td></tr>
        <tr><td style="padding:24px 32px 8px;font-family:{Font};color:#1b1b1f;">
        <h1 style="margin:0 0 12px;font-size:22px;font-weight:600;color:#1e3460;">{E(Heading)}</h1>
        <p style="margin:0 0 22px;font-size:15px;line-height:1.5;">{E(Intro)}</p>
        <div style="margin:0 0 18px;padding:18px 12px;background:#f2f2f2;border:1px solid #dedede;text-align:center;font-family:Consolas,Menlo,'Courier New',monospace;font-size:32px;letter-spacing:10px;color:#3a7a80;">{E(Code)}</div>
        <p style="margin:0 0 24px;font-size:14px;color:#5f6368;">It expires in {Minutes} minutes.</p>
        </td></tr>
        <tr><td style="padding:16px 32px 24px;border-top:1px solid #ececec;font-family:{Font};font-size:12px;line-height:1.5;color:#80868b;">
        {E(Footer)}<br><br>{E(Credit)}
        </td></tr>
        </table>
        </td></tr>
        </table>
        </body>
        </html>
        """;

    static string E(string text) => WebUtility.HtmlEncode(text);
}
