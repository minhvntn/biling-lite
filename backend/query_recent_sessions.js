const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const sessions = await prisma.session.findMany({
    orderBy: { startedAt: 'desc' },
    take: 5,
    include: { pc: true },
  });
  console.dir(sessions, { depth: null });
}

main().finally(() => prisma.$disconnect());
