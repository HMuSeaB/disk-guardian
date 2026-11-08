#include <windows.h>
#include <shellapi.h>

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow) {
    WCHAR baseDir[MAX_PATH];
    GetModuleFileNameW(NULL, baseDir, MAX_PATH);

    WCHAR *p = wcsrchr(baseDir, L'\\');
    if (p) *p = L'\0';

    WCHAR appExe[MAX_PATH];
    wsprintfW(appExe, L"%s\\app\\DiskGuardian.UI.exe", baseDir);

    if (GetFileAttributesW(appExe) == INVALID_FILE_ATTRIBUTES) {
        wsprintfW(appExe, L"%s\\DiskGuardian.UI.exe", baseDir);
    }

    if (GetFileAttributesW(appExe) == INVALID_FILE_ATTRIBUTES) {
        WCHAR msg[1024];
        wsprintfW(msg, L"找不到应用程序核心组件:\n%s\\app\\DiskGuardian.UI.exe\n\n请确认解压时完整保留了 app 目录及全部依赖文件。", baseDir);
        MessageBoxW(NULL, msg, L"DiskGuardian 启动错误", MB_OK | MB_ICONERROR);
        return 1;
    }

    WCHAR workDir[MAX_PATH];
    lstrcpyW(workDir, appExe);
    WCHAR *q = wcsrchr(workDir, L'\\');
    if (q) *q = L'\0';

    STARTUPINFOW si = { sizeof(si) };
    PROCESS_INFORMATION pi = { 0 };

    // 用 CreateProcessW 启动
    WCHAR cmdLine[MAX_PATH * 2];
    wsprintfW(cmdLine, L"\"%s\"", appExe);

    if (!CreateProcessW(appExe, cmdLine, NULL, NULL, FALSE, 0, NULL, workDir, &si, &pi)) {
        DWORD err = GetLastError();
        WCHAR errBuf[256];
        wsprintfW(errBuf, L"无法拉起应用 (错误代码: %lu)\n路径: %s", err, appExe);
        MessageBoxW(NULL, errBuf, L"启动失败", MB_OK | MB_ICONERROR);
        return 1;
    }

    CloseHandle(pi.hProcess);
    CloseHandle(pi.hThread);
    return 0;
}
