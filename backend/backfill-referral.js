const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const members = await prisma.member.findMany({
    where: { referralCode: null }
  });

  console.log(`Found ${members.length} members without referralCode. Backfilling...`);

  for (const m of members) {
    let newReferralCode = '';
    for (let i = 0; i < 5; i++) {
      newReferralCode = Math.random().toString(36).substring(2, 8).toUpperCase();
      const existing = await prisma.member.findUnique({ where: { referralCode: newReferralCode } });
      if (!existing) break;
    }

    await prisma.member.update({
      where: { id: m.id },
      data: { referralCode: newReferralCode }
    });
    console.log(`Updated member ${m.username} with code ${newReferralCode}`);
  }
}

main().catch(console.error).finally(() => prisma.$disconnect());
