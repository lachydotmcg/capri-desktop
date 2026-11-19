# Everyday jobs

Her reliability bar: 22 everyday jobs, each asked the way a person types it, run against a brain. A job passes when she uses the right tool with the right details and her reply is true to what the tool said. The table says how often each brain gets each job right.

The jobs live in `Jobs.cs`. Each has the ask, what a pass means, the fake results her tools give back (`Answer`), and the ideal tool calls and reply (`Ideal`) that the scripted brain plays.

## What it runs

The real brain code (`app/Brain.cs`, `app/Hands.cs`) from the cross build: her real system prompt, her real tool list, the real request to each provider, her hands' set phrases and the work-out loop. Only the computer is fake. `FakePc.cs` records each tool call and answers with a result like the real one (`Opened Discord.`, `Paused.`, a weather line, a list of files), so nothing on the machine changes and a job takes seconds.

It uses her Mac and Linux tool list, as the cross build has it, with the Linux terminal tools left out. Windows-only tools such as `pc_status` aren't in it, so a PC check accepts `linux_system` too.

## Brains

| Spec | Brain |
|---|---|
| `scripted` | No model. Plays each job's `Ideal`, so it checks the jobs and the harness. Runs in `ci/check-linux.sh` on every PR and must pass 100%. |
| `server:<address>\|<model>` | Her NORMAL brain or any OpenAI-compatible server (llama-server, Ollama, LM Studio), as a brain on this PC. |
| `key:<ENV_VAR>\|<model>` | A cloud key read from that environment variable. `default` picks the model she picks for a new key of that kind. |
| `claude-plan:<model>` | Their Claude plan through Claude Code, the way Windows' PlanBrain runs it (`haiku`, `sonnet` or `opus`). |

## Running it

```
dotnet run --project tests/everyday-jobs/EverydayJobs.csproj -c Release -- --brain "Scripted=scripted" --floor 100
dotnet run --project tests/everyday-jobs/EverydayJobs.csproj -c Release -- --brain "NORMAL=server:http://127.0.0.1:8080|capri" --warmup --out results/local.json
ANTHROPIC=sk-ant-... dotnet run --project tests/everyday-jobs/EverydayJobs.csproj -c Release -- --brain "Claude key=key:ANTHROPIC|default" --repeat 3 --out results/claude.json
dotnet run --project tests/everyday-jobs/EverydayJobs.csproj -c Release -- --merge results/*.json --md results/table.md
```

`--jobs id,id` runs some of them, `--repeat n` runs each job n times (cells then show 2/3), `--list` lists them.

The `Everyday jobs` workflow runs her NORMAL brain on the CPU (Gemma 4 E2B by default, E4B or Qwen3 4B from the Run workflow menu) on every `v*` tag, on PRs that change the jobs, and by hand. Cloud brains run when their key is saved as a repository secret: `CAPRI_EVAL_ANTHROPIC_KEY`, `CAPRI_EVAL_OPENAI_KEY`, `CAPRI_EVAL_NVIDIA_KEY`, `CAPRI_EVAL_GOOGLE_KEY`, `CAPRI_EVAL_DEEPSEEK_KEY`. The merged table is in the run's summary.

## Adding a job

Add a `Job` to `Jobs.All` with an `Id`, `Area`, `Ask`, `Goal`, a `Check` (null for a pass, or why it missed) and an `Ideal`, then run the scripted brain. Keep checks about what a person would call right: the tool and the detail that matters, not exact wording. Give tools that need a believable answer one in `Answer`, so the reply can be checked against it.
