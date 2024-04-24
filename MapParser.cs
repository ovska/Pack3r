using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
using Pack3r.IO;

namespace Pack3r;

public class MapParser(ILineReader reader)
{
    public async Task<HashSet<ReadOnlyMemory<char>>> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        State state = State.None;
        char expect = default;

        HashSet<ReadOnlyMemory<char>> shaders = new(ROMCharComparer.Instance);

        await foreach (var line in reader.ReadLines(path, new LineOptions(KeepRaw: true), cancellationToken))
        {
            if (expect != default)
            {
                if (line.FirstChar == expect && (line.Raw.Length == 1 || line.Value.Trim().Length == 1))
                {
                    expect = default;
                    continue;
                }

                ThrowHelper.ThrowInvalidDataException(
                    $"Expected '{expect}' on line {line.Index}, actual value: {line.Raw}");
            }

            if (line.FirstChar == '}')
            {
                state = state switch
                {
                    State.Entity => State.None,
                    State.AfterDef => State.Entity,
                    State.BrushDef => State.AfterDef,
                    State.PatchDef => State.AfterDef,
                    _ => ThrowHelper.ThrowInvalidOperationException<State>(
                        $"Invalid .map file, dangling closing bracket on line {line.Index}!")
                };
                continue;
            }

            if (state == State.None)
            {
                Expect("// entity ", in line);
                state = State.Entity;
                expect = '{';
                continue;
            }

            if (state == State.Entity)
            {
                // TODO: read keys and values

                if (line.FirstChar == 'b')
                {
                    if (line.Raw.Equals("brushDef"))
                    {
                        state = State.BrushDef;
                        expect = '{';
                        continue;
                    }
                }
                else if (line.FirstChar == 'p')
                {
                    if (line.Raw.Equals("patchDef2"))
                    {
                        state = State.PatchDef;
                        expect = '{';
                        continue;
                    }
                }
            }

            if (state == State.BrushDef)
            {
                int lastParen = line.Raw.LastIndexOf(')');
                ReadOnlyMemory<char> shaderPart = line.Raw.AsMemory(lastParen + 2);

                if (!CanSkip(shaderPart))
                {
                    int space = line.Raw.AsSpan(lastParen + 2).IndexOf(' ');

                    if (space > 1)
                    {
                        shaders.Add(line.Raw.AsMemory(lastParen + 2, space));
                    }
                    else
                    {
                        ThrowHelper.ThrowInvalidOperationException(
                            $"Malformed brush face definition on line {line.Index}: {line.Raw}");
                    }
                }

                continue;
            }

            if (state == State.PatchDef)
            {
                // skip patch cruft
                if (line.FirstChar is '(' or ')')
                {
                    continue;
                }

                // only non-paren starting line in a patchDef should be the texture
                shaders.Add(line.Value);
            }
        }

        return shaders;
    }

    /// <summary>
    /// Whether the shader is one of the most common ones and should be skipped.
    /// </summary>
    /// <param name="shaderPart"><c>pgm/holo 0 0 0</c></param>
    private static bool CanSkip(ReadOnlyMemory<char> shaderPart)
    {
        var span = shaderPart.Span;

        const string common = "common/";
        const string caulk = "caulk ";
        const string nodraw = "nodraw ";
        const string trigger = "trigger ";

        if (span.Length > 12 &&
            span.DangerousGetReference() == 'c' &&
            span.DangerousGetReferenceAt(6) == '/' &&
            span.StartsWith("common/"))
        {
            span = span[common.Length..];

            return span.DangerousGetReference() switch
            {
                'c' when span.StartsWith(caulk) => true,
                'n' when span.StartsWith(nodraw) => true,
                't' when span.StartsWith(trigger) => true,
                _ => false
            };
        }

        return false;
    }

    private static void Expect(string prefix, in Line line)
    {
        if (!line.Value.Span.StartsWith(prefix))
        {
            ThrowHelper.ThrowInvalidDataException(
                $"Expected line {line.Index} to start with \"{prefix}\", actual value: {line.Raw}");
        }
    }

    private enum State : byte
    {
        /// <summary>Top level, expecting entity</summary>
        None = 0,

        /// <summary>Entity header read, </summary>
        Entity = 1,

        /// <summary>BrushDef started</summary>
        BrushDef = 2,

        /// <summary>PatchDef started</summary>
        PatchDef = 3,

        /// <summary>BrushDef/PatchDef ended</summary>
        AfterDef = 4,
    }
}
