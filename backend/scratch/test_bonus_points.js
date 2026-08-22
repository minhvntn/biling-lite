const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const memberId = '8a37e1f5-d853-4c47-aed3-21ca512f3691'; // tong

  // let's simulate what buildLoyaltySnapshot computes
  const usageTxs = await prisma.memberTransaction.findMany({
    where: { memberId, createdBy: 'client.session.loyalty', playSecondsDelta: { lt: 0 } }
  });
  let consumed = 0;
  for (const t of usageTxs) consumed += Math.abs(t.playSecondsDelta);

  const bonusAgg = await prisma.memberTransaction.aggregate({
    _sum: { playSecondsDelta: true },
    where: {
      memberId,
      OR: [
        { note: { startsWith: 'MISSION_REWARD:' } },
        { note: { startsWith: 'LOYALTY_DAILY_CHECKIN:' } },
      ],
      playSecondsDelta: { gt: 0 }
    }
  });
  const bonusSecs = bonusAgg._sum.playSecondsDelta || 0;
  const bonusPts = Math.floor(bonusSecs / 1200); // since 20 min/point

  console.log('Consumed usage secs:', consumed, '-> points:', Math.floor(consumed / 1200));
  console.log('Bonus secs from missions/checkin:', bonusSecs, '-> points:', bonusPts);
  console.log('Total Earned Points:', Math.floor(consumed / 1200) + bonusPts);
}

main().finally(() => prisma.$disconnect());
