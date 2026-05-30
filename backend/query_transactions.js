const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const member = await prisma.member.findFirst({
    where: { username: 'test' }
  });
  if (!member) {
    console.log("No test member found");
    return;
  }
  console.log("Member:", JSON.stringify(member, null, 2));

  const transactions = await prisma.memberTransaction.findMany({
    where: { memberId: member.id },
    orderBy: { createdAt: 'desc' }
  });
  console.log("Transactions for 'test':", JSON.stringify(transactions, null, 2));

  const promos = await prisma.timeBasedPromotion.findMany();
  console.log("Promotions:", JSON.stringify(promos, null, 2));

  const settings = await prisma.appSetting.findMany();
  console.log("Settings:", JSON.stringify(settings, null, 2));
}

main().finally(() => prisma.$disconnect());
