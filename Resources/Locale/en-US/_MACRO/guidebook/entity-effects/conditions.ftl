entity-condition-guidebook-status-effect-duration =
    { $max ->
        [2147483648] the target has at least {NATURALFIXED($min, 3)} {MANY("second", $min)} of {$effect}
        *[other] { $min ->
                    [0] the target has at most {NATURALFIXED($max, 3)} {MANY("second", $max)} of {$effect}
                    *[other] the target has between {NATURALFIXED($min, 3)} and {NATURALFIXED($max, 3)} {MANY("second", $max)} of {$effect}
                 }
    }
