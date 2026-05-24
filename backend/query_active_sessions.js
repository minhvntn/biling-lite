const { PrismaClient } = require('@prisma/client');
const prisma = new PrismaClient();

async function main() {
  const sessions = await prisma.session.findMany({
    where: { status: 'ACTIVE' },
    include: { pc: true },
  });
  console.dir(sessions, { depth: null });
}

main().finally(() => prisma.$disconnect());
