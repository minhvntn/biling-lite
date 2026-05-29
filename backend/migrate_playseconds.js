const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const members = await prisma.member.findMany({
    where: {
      playSeconds: {
        gt: 0
      }
    }
  });

  console.log(`Found ${members.length} members with playSeconds > 0.`);

  for (const member of members) {
    const playSecondsDelta = member.playSeconds;
    const cashDelta = Math.round((playSecondsDelta / 3600) * 6000);
    console.log(`Migrating member "${member.username}" (${member.id}):`);
    console.log(`  Current playSeconds: ${playSecondsDelta} seconds`);
    console.log(`  Current balance: ${member.balance} VND`);
    console.log(`  Converting to cash: +${cashDelta} VND`);

    await prisma.$transaction(async (tx) => {
      await tx.member.update({
        where: { id: member.id },
        data: {
          balance: {
            increment: cashDelta
          },
          playSeconds: 0
        }
      });

      await tx.memberTransaction.create({
        data: {
          memberId: member.id,
          type: 'ADJUSTMENT',
          amountDelta: cashDelta,
          playSecondsDelta: -playSecondsDelta,
          note: 'CONVERT_PLAYSECONDS_TO_BALANCE',
          createdBy: 'migration'
        }
      });
    });

    console.log(`  Successfully migrated "${member.username}".`);
  }

  console.log('Migration completed successfully.');
}

main()
  .catch(console.error)
  .finally(() => prisma.$disconnect());
