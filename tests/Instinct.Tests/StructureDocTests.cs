using System.Text.RegularExpressions;
using Instinct.Sim;
using Xunit;

namespace Instinct.Tests;

/// <summary>
/// STRUCTURE.md(구조도)가 코드보다 뒤처지면 실패한다.
/// 제도·본능·소스 파일·한 철의 단계·실행 옵션을 새로 넣고 구조도에 적지 않으면 여기서 걸린다.
/// </summary>
public class StructureDocTests
{
    static readonly string Root = FindRoot();
    static readonly string Doc = File.ReadAllText(Path.Combine(Root, "STRUCTURE.md"));

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "STRUCTURE.md"))) return d.FullName;
        throw new FileNotFoundException("STRUCTURE.md를 찾지 못했다. 프로젝트 루트에 있어야 한다.");
    }

    static void AssertMentioned(IEnumerable<string> names, string what)
    {
        var missing = names.Where(n => !Regex.IsMatch(Doc, $@"(?<![A-Za-z_]){Regex.Escape(n)}(?![A-Za-z_])")).ToList();
        Assert.True(missing.Count == 0,
            $"STRUCTURE.md에 빠진 {what}: {string.Join(", ", missing)} — 구조도를 먼저 고쳐라.");
    }

    [Fact]
    public void 모든_제도가_구조도에_있다() =>
        AssertMentioned(typeof(Institutions).GetFields().Select(f => f.Name), "제도");

    [Fact]
    public void 모든_본능이_구조도에_있다() =>
        AssertMentioned(Enum.GetNames<Trait>(), "본능");

    [Fact]
    public void 모든_소스_파일이_구조도에_있다() =>
        AssertMentioned(
            Directory.GetFiles(Path.Combine(Root, "src"), "*.cs", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(Root, "tests"), "*.cs", SearchOption.AllDirectories))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                            && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                .Select(Path.GetFileName)!,
            "소스 파일");

    [Fact]
    public void 한_철의_모든_단계가_구조도에_있다()
    {
        string src = File.ReadAllText(Path.Combine(Root, "src", "Instinct.Sim", "Simulation.cs"));
        var body = Regex.Match(src, @"public void Step\(\)\s*\{(.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(body.Success, "Step() 본문을 찾지 못했다.");
        var steps = Regex.Matches(body.Groups[1].Value, @"\b([A-Z]\w+)\(\);").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(steps.Count >= 10, "Step()에서 단계를 읽어내지 못했다.");
        AssertMentioned(steps, "한 철의 단계");
    }

    [Fact]
    public void 모든_실행_옵션이_구조도에_있다()
    {
        string src = File.ReadAllText(Path.Combine(Root, "src", "Instinct.Runner", "Program.cs"));
        var flags = Regex.Matches(src, @"case ""(--[a-z]+|[a-z]+)"":").Select(m => m.Groups[1].Value.TrimStart('-')).Distinct();
        AssertMentioned(flags.Select(f => "--" + f), "실행 옵션");
    }

    [Fact]
    public void 이웃의_모든_상태가_구조도에_있다() =>
        AssertMentioned(typeof(Neighbor).GetFields().Select(f => f.Name), "이웃 상태");

    [Fact]
    public void 이웃_나라의_모든_상태가_구조도에_있다() =>
        AssertMentioned(typeof(Rival).GetFields().Select(f => f.Name), "이웃 나라 상태");

    [Fact]
    public void 나라의_모든_상태가_구조도에_있다() =>
        AssertMentioned(typeof(Realm).GetFields().Select(f => f.Name), "나라 상태");

    [Fact]
    public void 모든_계층이_구조도에_있다() =>
        AssertMentioned(Enum.GetNames<Estate>().Concat(Catalog.EstateNames), "계층");
}
