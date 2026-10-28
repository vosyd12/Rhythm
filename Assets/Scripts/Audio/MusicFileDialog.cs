using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

// 실행 환경에 맞는 파일 선택창을 열고,
// 선택한 음원 파일의 전체 경로를 반환한다.
public static class MusicFileDialog
{
    public static string OpenAudioFile()
    {
#if UNITY_EDITOR
        // 에디터에서는 기존 Unity 파일 선택창을 사용한다.
        return UnityEditor.EditorUtility.OpenFilePanelWithFilters(
            "Select Music",
            "Assets",
            new string[]
            {
                "Audio Files", "mp3,wav,ogg"
            });

#elif UNITY_STANDALONE_WIN
        // Windows 실행파일에서는 Windows 기능을 직접 호출한다.
        return OpenWindowsAudioFile();

#else
        Debug.LogWarning(
            "현재 플랫폼에서는 파일 선택창을 지원하지 않습니다.");

        return string.Empty;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR

    // 선택한 파일 경로를 저장할 최대 문자 수.
    // 마지막 문자열 종료 문자까지 포함하는 공간이다.
    private const int PathCapacity = 32768;

    // Windows에 전달할 파일 선택 옵션.
    private const uint Explorer = 0x00080000;
    private const uint FileMustExist = 0x00001000;
    private const uint PathMustExist = 0x00000800;
    private const uint HideReadOnly = 0x00000004;

    // Windows의 OPENFILENAME 구조체와 같은 순서와 자료형으로 배치한다.
    //
    // Sequential: 선언한 순서를 기준으로 메모리에 배치한다.
    // Unicode: 문자열을 UTF-16으로 전달한다.
    //
    // 사용하지 않는 항목도 구조체 배치를 맞추기 위해 필요하다.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName
    {
        public uint structSize;
        public IntPtr owner;
        public IntPtr instance;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string filter;

        public IntPtr customFilter;
        public uint maxCustomFilter;
        public uint filterIndex;

        public IntPtr file;
        public uint maxFile;

        public IntPtr fileTitle;
        public uint maxFileTitle;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string initialDirectory;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string title;

        public uint flags;
        public ushort fileOffset;
        public ushort fileExtension;

        public IntPtr defaultExtension;
        public IntPtr customData;
        public IntPtr hook;
        public IntPtr templateName;
        public IntPtr reserved;

        public uint reservedValue;
        public uint flagsEx;
    }

    // Windows에 있는 파일 선택 함수를 C#에서 호출하도록 연결한다.
    // W는 Unicode 문자열을 사용하는 함수라는 뜻이다.
    [DllImport(
        "comdlg32.dll",
        CharSet = CharSet.Unicode,
        ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetOpenFileNameW(
        ref OpenFileName data);

    // 취소와 실제 오류를 구분할 때 사용한다.
    [DllImport("comdlg32.dll", ExactSpelling = true)]
    private static extern uint CommDlgExtendedError();

    // 현재 스레드의 활성 창을 조회한다.
    // 파일 선택창의 부모 창을 지정하는 데 사용한다.
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetActiveWindow();

    private static string OpenWindowsAudioFile()
    {
        string previousDirectory = Directory.GetCurrentDirectory();

        // UTF-16 문자 하나는 2바이트다.
        // Windows가 결과 경로를 써 넣을 메모리를 직접 확보한다.
        IntPtr pathBuffer = Marshal.AllocHGlobal(
            PathCapacity * sizeof(char));

        try
        {
            // 첫 문자를 0으로 설정하면 빈 문자열이 된다.
            // 파일 이름 입력칸을 비운 상태로 시작한다.
            Marshal.WriteInt16(pathBuffer, 0);

            OpenFileName data = new OpenFileName
            {
                structSize =
                    (uint)Marshal.SizeOf(typeof(OpenFileName)),

                owner = GetActiveWindow(),

                // 표시 이름과 확장자 조건을 \0으로 구분한다.
                // 여러 확장자는 세미콜론으로 구분한다.
                // 전체 필터의 끝은 \0\0으로 표시한다.
                filter =
                    "Audio Files\0*.mp3;*.wav;*.ogg\0\0",

                filterIndex = 1,

                file = pathBuffer,
                maxFile = (uint)PathCapacity,

                initialDirectory = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyMusic),

                title = "Select Music",

                // |는 여러 옵션을 함께 사용하는 비트 OR 연산이다.
                // 실제로 존재하는 파일과 경로를 선택하도록 설정한다.
                flags =
                    Explorer |
                    FileMustExist |
                    PathMustExist |
                    HideReadOnly
            };

            // 파일 선택창을 닫을 때까지 이 호출이 기다린다.
            if (GetOpenFileNameW(ref data))
            {
                // Windows가 기록한 UTF-16 경로를
                // C#에서 사용하는 문자열로 복사한다.
                return Marshal.PtrToStringUni(pathBuffer)
                    ?? string.Empty;
            }

            uint errorCode = CommDlgExtendedError();

            // 오류 코드가 0이면 사용자가 취소한 것이다.
            if (errorCode != 0)
            {
                Debug.LogError(
                    $"음원 파일 선택창 오류: 0x{errorCode:X8}");
            }

            return string.Empty;
        }
        finally
        {
            // 성공, 취소, 예외 발생 여부와 관계없이 메모리를 해제한다.
            Marshal.FreeHGlobal(pathBuffer);

            // 파일 탐색 중 작업 폴더가 바뀌었을 경우 원래대로 복구한다.
            if (Directory.Exists(previousDirectory))
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
    }

#endif
}