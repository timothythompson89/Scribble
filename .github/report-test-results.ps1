# Says which tests failed, where it can be read without the log: GitHub shows a job's log only to signed-in
# users, but its annotations and summary to everyone (and through the public API). Run after `dotnet test
# --logger trx`, whether the tests passed or not; it never fails the job itself (the test step already did).
#
#     pwsh .github/report-test-results.ps1 TestResults
#
# For each .trx file: an error annotation per failed test (name, message, the first lines of the stack), and a
# line of totals in the job summary. A run with no .trx file at all (the test host died before writing one) is
# reported as that.
param([string] $ResultsDirectory = "TestResults")

function Format-Annotation([string] $text) {
    # A workflow command is one line: GitHub's escapes for %, CR and LF.
    $text.Replace("%", "%25").Replace("`r", "%0D").Replace("`n", "%0A")
}

$summary = New-Object System.Collections.Generic.List[string]
$files = @(Get-ChildItem $ResultsDirectory -Recurse -Filter *.trx -ErrorAction SilentlyContinue)

if ($files.Count -eq 0) {
    Write-Host "::error title=No test results::No .trx file under '$ResultsDirectory': the test run ended before writing one (a crash or a hang)."
    $summary.Add("**No test results were written** (the run crashed or hung before finishing).")
}

foreach ($file in $files) {
    # A run that died while writing leaves a file that isn't XML: say so and go on.
    try { [xml] $trx = Get-Content $file.FullName -Raw }
    catch {
        Write-Host "::error title=Unreadable test results::$($file.Name) is not valid XML: the test run ended while writing it."
        $summary.Add("**$($file.Name) could not be read** (the run ended while writing it).")
        continue
    }
    $counters = $trx.TestRun.ResultSummary.Counters
    $outcome = $trx.TestRun.ResultSummary.outcome
    $skipped = [int] $counters.total - [int] $counters.executed
    $summary.Add("**$outcome**: $($counters.passed) passed, $($counters.failed) failed, $skipped skipped, $($counters.total) total (``$($file.Name)``)")

    $failed = @($trx.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq "Failed" })
    foreach ($result in $failed) {
        $message = [string] $result.Output.ErrorInfo.Message
        $stack = (([string] $result.Output.ErrorInfo.StackTrace) -split "`n" | Select-Object -First 6) -join "`n"
        $title = Format-Annotation "Test failed: $($result.testName)"
        Write-Host "::error title=$title::$(Format-Annotation ($message + "`n" + $stack))"
        $summary.Add("")
        $summary.Add("- ``$($result.testName)``")
        $summary.Add("")
        $summary.Add("  ``````")
        foreach ($line in ($message + "`n" + $stack) -split "`n") { $summary.Add("  " + $line.TrimEnd()) }
        $summary.Add("  ``````")
    }

    # What the run itself reported as an error (an aborted run, a crashed test host). Its warnings are left out:
    # every skipped test is one.
    foreach ($info in @($trx.TestRun.ResultSummary.RunInfos.RunInfo)) {
        if ($info -and $info.outcome -eq "Error" -and $info.Text) {
            Write-Host "::error title=Test run::$(Format-Annotation ([string] $info.Text))"
            $summary.Add("")
            $summary.Add("> " + ([string] $info.Text).Trim().Replace("`n", " "))
        }
    }
}

$text = "### Unit tests`n`n" + ($summary -join "`n") + "`n"
if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $text -Encoding utf8 } else { Write-Host $text }
exit 0
