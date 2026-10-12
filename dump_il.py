import sys
from pathlib import Path

import dnfile
from dncil.cil.body.reader import read_method_body_from_bytes
from dncil.clr.token import StringToken, Token


path = Path(sys.argv[1])
pe = dnfile.dnPE(str(path))


def row_name(row):
    if row is None:
        return "<?>"
    for attr in ("Name", "TypeName"):
        value = getattr(row, attr, None)
        if value:
            namespace = getattr(row, "TypeNamespace", None)
            return f"{namespace}.{value}" if namespace else str(value)
    return row.__class__.__name__


def resolve(operand):
    if isinstance(operand, StringToken):
        try:
            return repr(str(pe.net.user_strings.get(operand.rid)))
        except Exception:
            return str(operand)
    if isinstance(operand, Token):
        try:
            table = pe.net.mdtables.tables[operand.table]
            row = table.rows[operand.rid - 1]
            return f"{operand} {table.name}:{row_name(row)}"
        except Exception:
            return str(operand)
    return repr(operand) if isinstance(operand, str) else str(operand)


selected = {
    "Baccarat.Authentication": {"Initialize", "CheckToken", "Signin", "SignUp", "Signout", "ValidateCookies", "WriteCookies", ".cctor"},
    "Baccarat.WS_Authentication": {"Initialize", "CheckToken", "Signin", "SignUp", "Signout", "ValidateServices", "WriteCookies", "ExtractJson", "SendData", ".cctor"},
    "Baccarat.Global": {"LaunchBrowser", "LaunchHeadlessBrowser", "StartChrome", "StartChromeHeadless", "StartSeleniumHeadless", "DownloadLatestVersionOfChromeDriver", "GetInternetDate", "Withdraw", "TwoFactorCheck", "GetCoinAddresses", "Encrypt", "Decrypt", ".cctor"},
    "Baccarat.SignIn": {".ctor", "i08fhOne1M", ".cctor"},
    "Baccarat.Program": {"Main", ".cctor"},
    "Updater.ConfirmUpdate": {"btnProceed_Click", "ConfirmUpdate_Load", "_timer_Elapsed"},
    "Updater.DownloadUpdate": {"DownloadUpdate_Load", "btnClose_Click"},
    "Updater.Program": {"Main"},
    "Updater.Updates": {"CheckForUpdates", "IsNewVersion", "GetOldVersion", "GetNewVersion", "CheckForInternetConnection", "UpdateFiles", "KillProcess", "DownloadLatest", "GetTemporaryDirectory", "DirectoryCopy", "SetNewVersion", "SetPermissions", ".cctor"},
}

for type_row in pe.net.mdtables.TypeDef.rows:
    fullname = f"{type_row.TypeNamespace}.{type_row.TypeName}".strip(".")
    wanted = selected.get(fullname)
    # These routines are referenced by the recovered static initializers.
    decoder_names = {
        "lj2CnC3XCe", "Bt3CZBbOLm", "NBZCSYshtE", "y6YCC0oNi8",
        # Version 2 string/configuration decoder and startup protection routines.
        "NASP55YLMI", "jcCPYVXWLf", "GMyP2yNkCu", "iIJPh5Y8ZT",
        "LjOi6fLzJ3cl7", "rBAPS82UKfeKS",
    }
    if not wanted:
        wanted = {str(m.row.Name) for m in type_row.MethodList} & decoder_names
    if not wanted:
        continue
    print(f"\n===== TYPE {fullname} =====")
    for method_index in type_row.MethodList:
        method = method_index.row
        name = str(method.Name)
        if name not in wanted:
            continue
        print(f"\n--- METHOD {name} RVA=0x{method.Rva:X} ---")
        if not method.Rva:
            print("<no body>")
            continue
        try:
            body = read_method_body_from_bytes(pe.get_data(method.Rva, 0x10000))
            for ins in body.instructions:
                operand = "" if ins.operand is None else " " + resolve(ins.operand)
                print(f"IL_{ins.offset:04X}: {ins.opcode.name}{operand}")
        except Exception as exc:
            print(f"<parse error: {exc!r}>")
