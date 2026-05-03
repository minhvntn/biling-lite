import { PrismaClient } from '@prisma/client';
const prisma = new PrismaClient();
async function check() {
  const ranks = await prisma.loyaltyRankConfig.findMany();
  console.log('Ranks in DB:', JSON.stringify(ranks, null, 2));
  const members = await prisma.member.findMany({ select: { username: true, totalTopup: true } });
  console.log('Members totalTopup:', JSON.stringify(members, null, 2));
}
check().then(() => prisma.$disconnect());
