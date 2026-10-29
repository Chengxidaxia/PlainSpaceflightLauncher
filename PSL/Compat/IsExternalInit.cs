// .NET Framework 4.8 没有 System.Runtime.CompilerServices.IsExternalInit，
// C# 9 的 record / init 访问器需要它，这里补一个编译器可见的声明。
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}
