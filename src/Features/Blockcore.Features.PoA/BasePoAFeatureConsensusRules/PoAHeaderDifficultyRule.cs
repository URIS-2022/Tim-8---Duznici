﻿using Blockcore.Consensus.Rules;
using NBitcoin;

namespace Blockcore.Features.PoA.BasePoAFeatureConsensusRules
{
    /// <summary>
    /// All PoA blocks should be of constant difficulty so chainwork always
    /// raises by constant amount with each new block.
    /// </summary>
    /// <remarks>
    /// Having this rule guarantees that longest chain will always be
    /// the best chain in terms of chainwork.
    /// </remarks>
    public class PoAHeaderDifficultyRule : HeaderValidationConsensusRule
    {
        public static readonly Target PoABlockDifficulty = new(uint256.Parse("00000000ffff0000000000000000000000000000000000000000000000000000"));

        public override void Run(RuleContext context)
        {
            if (context.ValidationContext.ChainedHeaderToValidate.Header.Bits != PoABlockDifficulty)
            {
                PoAConsensusErrors.InvalidHeaderBits.Throw();
            }
        }
    }
}
